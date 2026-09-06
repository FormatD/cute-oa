import { expect, request as apiRequest, test, type Page } from '@playwright/test'

const apiBase = 'http://127.0.0.1:5235/api/v1'
const password = 'Oa@123456'

async function loginApi(userId: string) {
  const context = await apiRequest.newContext()
  try {
    const response = await context.post(`${apiBase}/auth/login`, { data: { userId, password } })
    expect(response.ok()).toBeTruthy()
    const result = await response.json()
    expect(result.status).toBe('AUTHENTICATED')
    return result.session.accessToken as string
  } finally {
    await context.dispose()
  }
}

async function loginUi(page: Page, userId: string) {
  await page.goto('/#/login')
  const account = page.locator('select[autocomplete="username"]')
  await expect(account).toBeVisible()
  await account.selectOption(userId)
  await page.locator('input[autocomplete="current-password"]').fill(password)
  await page.getByRole('button', { name: '登录', exact: true }).click()
  await expect(page.getByRole('heading', { name: /你好/ })).toBeVisible()
}

function nextWeekday(seed: string) {
  const date = new Date()
  date.setUTCDate(date.getUTCDate() + 90 + Number.parseInt(seed.slice(0, 6), 16) % 1000)
  while (date.getUTCDay() === 0 || date.getUTCDay() === 6) date.setUTCDate(date.getUTCDate() + 1)
  return date.toISOString().slice(0, 10)
}

test('员工发起请假后，主管可在统一事项中心审批并进入已办', async ({ page, request }) => {
  const token = await loginApi('u-zhang')
  const runId = crypto.randomUUID().replaceAll('-', '')
  const reason = `E2E统一事项-${runId.slice(0, 8)}`
  const leaveDate = nextWeekday(runId)
  const created = await request.post(`${apiBase}/leave-requests`, {
    headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() },
    data: { type: 'Personal', startDate: leaveDate, startPeriod: 'FullDay', endDate: leaveDate, endPeriod: 'FullDay', reason, attachments: [], copyRecipientIds: [] }
  })
  expect(created.status()).toBe(201)
  const leave = await created.json()
  const submitted = await request.post(`${apiBase}/leave-requests/${leave.id}/submit`, {
    headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() }, data: {}
  })
  expect(submitted.ok(), await submitted.text()).toBeTruthy()

  await loginUi(page, 'u-li')
  await page.getByRole('button', { name: /事项中心/ }).click()
  await expect(page.getByRole('heading', { name: '事项中心' })).toBeVisible()
  await page.getByLabel('关键词').fill(reason)
  await page.getByRole('button', { name: '查询' }).click()
  const row = page.locator('tr', { hasText: reason })
  await expect(row).toBeVisible()
  await row.getByRole('button', { name: '同意' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.getByRole('dialog').getByRole('button', { name: '确认同意' }).click()
  await expect(row).toHaveCount(0)
  await page.getByRole('button', { name: /我的已办/ }).click()
  await expect(page.locator('tr', { hasText: reason })).toBeVisible()
})

test('角色化工作台与服务端权限范围保持一致', async ({ page, request }) => {
  const employeeToken = await loginApi('u-zhang')
  const risks = await request.get(`${apiBase}/work-items?tab=risk&page=1&pageSize=100`, { headers: { Authorization: `Bearer ${employeeToken}` } })
  expect(risks.ok()).toBeTruthy()
  const riskBody = await risks.json()
  expect(riskBody.items.every((item: { applicantId?: string }) => item.applicantId === 'u-zhang')).toBeTruthy()

  await loginUi(page, 'u-zhang')
  await expect(page.getByText('待我处理', { exact: true }).first()).toBeVisible()
  await expect(page.getByText('公告、制度与审批抄送')).toBeVisible()
  await page.getByRole('button', { name: /事项中心/ }).click()
  await expect(page.getByText(/当前角色：员工/)).toBeVisible()
  for (const name of ['待我处理', '我的已办', '我发起的', '待阅与已阅', '风险提醒'])
    await expect(page.getByRole('button', { name: new RegExp(name) })).toBeVisible()
})

test('流程管理员可配置节点 SLA 并在发布前试算路由', async ({ page, request }) => {
  const token = await loginApi('u-admin')
  const code = `E2E_SLA_${crypto.randomUUID().replaceAll('-', '').slice(0, 10).toUpperCase()}`
  const name = `E2E SLA 流程 ${code.slice(-4)}`
  const created = await request.post(`${apiBase}/process/definitions`, {
    headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() },
    data: {
      code, name, businessType: 'Leave', priority: 500, departmentIds: [], leaveTypes: [],
      routes: [{ maxValue: null, approverKeys: ['DIRECT_MANAGER'], nodePolicies: [{ handlingHours: 8, reminderBeforeHours: 2, escalateAfterHours: 4, escalationTarget: 'PROCESS_ADMIN', missingAssigneeAction: 'BLOCK', allowAutoSkip: false }] }]
    }
  })
  expect(created.status(), await created.text()).toBe(201)
  const definition = await created.json()
  const simulated = await request.post(`${apiBase}/process/definitions/${definition.id}/simulate`, {
    headers: { Authorization: `Bearer ${token}` }, data: { applicantId: 'u-zhang', metric: 1, category: 'Personal' }
  })
  expect(simulated.ok(), await simulated.text()).toBeTruthy()
  expect((await simulated.json()).approvers[0].policy.handlingHours).toBe(8)

  await loginUi(page, 'u-admin')
  await page.goto('/#/processes')
  await expect(page.getByRole('heading', { name: '流程定义' })).toBeVisible()
  const row = page.locator('tr', { hasText: name })
  await expect(row).toBeVisible()
  await row.getByRole('button', { name: '编辑' }).click()
  await expect(page.getByText('办理时限（小时）')).toBeVisible()
  await page.getByLabel('申请人').selectOption('u-zhang')
  await page.getByRole('button', { name: '试算路由' }).click()
  await expect(page.getByText(new RegExp(`命中 ${code}`))).toBeVisible()
  await expect(page.getByText('李薇 (8h)')).toBeVisible()
})

test('手机宽度可通过折叠菜单进入事项中心且页面不产生整页横向滚动', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await loginUi(page, 'u-zhang')
  await page.getByRole('button', { name: '打开导航' }).click()
  await page.getByRole('button', { name: /事项中心/ }).click()
  await expect(page.getByRole('heading', { name: '事项中心' })).toBeVisible()
  await expect(page.getByRole('button', { name: /待阅与已阅/ })).toBeVisible()
  const hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasPageOverflow).toBeFalsy()
})

test('管理员可在业务参数配置中心查看规则、切换UI与JSON快照、新建草稿并发布生效与升版', async ({ page }) => {
  await loginUi(page, 'u-admin')
  await page.goto('/#/business-configurations')
  await expect(page.getByRole('heading', { name: '业务参数配置中心' })).toBeVisible()

  // 1. 业务域标签切换与默认生效配置核验
  await expect(page.getByRole('button', { name: '全部域' })).toBeVisible()
  await page.getByRole('button', { name: '休假规则' }).click()
  const leaveRow = page.locator('tr', { hasText: 'LeavePolicy' }).first()
  await expect(leaveRow).toBeVisible()

  // 2. 查看配置详情，测试结构化 UI 回显与原始 JSON 快照双模式切换
  await leaveRow.getByRole('button', { name: '详情' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByText('业务参数生效配置')).toBeVisible()
  await expect(page.getByRole('button', { name: '结构化 UI 回显' })).toBeVisible()
  await page.getByRole('button', { name: '原始 JSON 快照' }).click()
  await expect(page.locator('pre.json-code-block')).toBeVisible()
  await page.getByRole('dialog').getByLabel('关闭').click()
  await expect(page.getByRole('dialog')).toHaveCount(0)

  // 3. 新建业务配置草稿
  const uniqueCode = `E2E_CFG_${crypto.randomUUID().replaceAll('-', '').slice(0, 8).toUpperCase()}`
  const uniqueName = `E2E 休假规则 ${uniqueCode.slice(-4)}`
  await page.getByRole('button', { name: '＋ 新建配置草稿' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.getByPlaceholder('例如: LeavePolicy, ExpensePolicy').fill(uniqueCode)
  await page.getByPlaceholder('请输入直观名称，如：全员休假规则').fill(uniqueName)
  await page.getByRole('dialog').getByRole('button', { name: '保存草稿' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)

  // 4. 列表查询新草稿并核对草稿状态
  await page.getByRole('button', { name: '全部域' }).click()
  await page.getByPlaceholder('搜索配置标识 / 名称 / 描述').fill(uniqueCode)
  await page.getByRole('button', { name: '查询' }).click()
  const draftRow = page.locator('tr', { hasText: uniqueCode })
  await expect(draftRow).toBeVisible()
  await expect(draftRow.getByText('草稿')).toBeVisible()

  // 5. 发布草稿使其立即生效
  await draftRow.getByRole('button', { name: '发布' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.getByRole('dialog').getByRole('button', { name: '确认发布' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(draftRow.getByText('生效中')).toBeVisible()

  // 6. 基于生效版本创建新版本升版草稿
  await draftRow.getByRole('button', { name: '新版本' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('dialog').getByText(/编辑配置草稿 \(v2\)/)).toBeVisible()
  await page.getByRole('dialog').getByLabel('关闭').click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
})

test('普通员工在导航侧边栏不显示业务参数配置中心入口', async ({ page }) => {
  await loginUi(page, 'u-zhang')
  await expect(page.getByRole('button', { name: /业务参数配置/ })).toHaveCount(0)
})

test('在 390px 移动视口下业务参数配置中心不产生整页横向滚动', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await loginUi(page, 'u-admin')
  await page.goto('/#/business-configurations')
  await expect(page.getByRole('heading', { name: '业务参数配置中心' })).toBeVisible()

  const hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasPageOverflow).toBeFalsy()

  const firstRow = page.locator('tbody tr').first()
  await firstRow.getByRole('button', { name: '详情' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()

  const hasDialogPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasDialogPageOverflow).toBeFalsy()

  await page.getByRole('dialog').getByLabel('关闭').click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
})


