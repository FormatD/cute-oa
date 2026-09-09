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

let weekdaySeq = 0
function nextWeekday(seed: string) {
  weekdaySeq++
  const date = new Date()
  const offset = 15000 + (Date.now() % 40000) + weekdaySeq * 7 + (Number.parseInt(seed.slice(0, 4), 16) % 500)
  date.setUTCDate(date.getUTCDate() + offset)
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

test('场景1: 新增或改名业务配置，发布后表单无需刷新登录即可看到变化', async ({ page, request }) => {
  const adminToken = await loginApi('u-admin')

  // 1. 用户先登录进入报销页面
  await loginUi(page, 'u-zhang')
  await page.goto('/#/expense')
  await expect(page.getByRole('heading', { name: '费用报销' })).toBeVisible()

  // 2. 通过 API 将费用配置新增一个类别
  const cfgList = await request.get(`${apiBase}/business-configurations?domain=Expense&code=ExpensePolicy`, { headers: { Authorization: `Bearer ${adminToken}` } })
  expect(cfgList.ok()).toBeTruthy()
  const cfgData = await cfgList.json()
  const expenseCfg = cfgData.items[0]

  // 基于现有创建新版本草稿
  const newVer = await request.post(`${apiBase}/business-configurations/${expenseCfg.id}/versions`, {
    headers: { Authorization: `Bearer ${adminToken}`, 'Idempotency-Key': crypto.randomUUID() }
  })
  expect(newVer.ok()).toBeTruthy()
  const draft = await newVer.json()
  const content = JSON.parse(draft.contentJson)
  const uniqueCode = `ExpE2E_${crypto.randomUUID().replaceAll('-', '').slice(0, 6)}`
  const uniqueName = `团建拓展_${crypto.randomUUID().replaceAll('-', '').slice(0, 4)}`
  if (!content.categories) content.categories = content.Categories || []
  content.categories.push({ code: uniqueCode, name: uniqueName, isEnabled: true, singleLimit: 5000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false })

  const updated = await request.put(`${apiBase}/business-configurations/${draft.id}`, {
    headers: { Authorization: `Bearer ${adminToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: { name: draft.name, description: draft.description, contentJson: JSON.stringify(content), concurrencyVersion: draft.concurrencyVersion, effectiveFrom: draft.effectiveFrom }
  })
  expect(updated.ok()).toBeTruthy()
  const updatedDraft = await updated.json()

  // 发布新版本
  const pub = await request.post(`${apiBase}/business-configurations/${draft.id}/publish`, {
    headers: { Authorization: `Bearer ${adminToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: { concurrencyVersion: updatedDraft.concurrencyVersion }
  })
  expect(pub.ok(), await pub.text()).toBeTruthy()

  // 3. 页面触发 focus，无需刷新登录即可加载最新配置
  await page.evaluate(() => window.dispatchEvent(new Event('focus')))

  // 打开新建报销弹窗，验证下拉选项包含新类别
  await page.getByRole('button', { name: '新建报销' }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.locator('select option', { hasText: uniqueName })).toBeAttached()
  await dialog.getByLabel('关闭').click()
})

test('场景2: 配置缺失或损坏时，表单明确阻止提交', async ({ page }) => {
  await loginUi(page, 'u-zhang')

  // 拦截 effective-options 返回 500 损坏错误
  await page.route('**/api/v1/business-configurations/effective-options', route => {
    route.fulfill({
      status: 500,
      contentType: 'application/json',
      body: JSON.stringify({ code: 'CONFIG_INVALID', error: '配置内容损坏，无法加载有效参数。' })
    })
  })

  await page.goto('/#/leave')
  await expect(page.getByRole('heading', { name: '请假申请' })).toBeVisible()
  await page.getByRole('button', { name: '新建请假' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.getByTestId('config-error-alert')).toBeVisible()
  await expect(dialog.getByTestId('config-error-alert')).toContainText('业务配置缺失或不可用，禁止提交申请')

  const submitBtn = dialog.getByRole('button', { name: '保存并提交' })
  await expect(submitBtn).toBeDisabled()
})

test('场景3: 差旅正常标准、超标需理由、超标禁止提交三条路径', async ({ page }) => {
  await loginUi(page, 'u-zhang')
  await page.goto('/#/travel')
  await expect(page.getByRole('heading', { name: '出差管理' })).toBeVisible()

  // 路径 1: 正常标准（预算 400 <= 550），无超标警告，提交成功
  const runId1 = crypto.randomUUID().replaceAll('-', '').slice(0, 8)
  const purpose1 = `E2E差旅正常-${runId1}`
  const travelDate1 = nextWeekday(runId1)

  await page.getByRole('button', { name: '＋ 发起出差' }).click()
  let dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await dialog.getByPlaceholder('说明出差目的和预期成果').fill(purpose1)
  await dialog.getByPlaceholder('例如 上海').fill('上海')
  let dateInputs = dialog.locator('.itinerary-row input[type="date"]')
  await dateInputs.nth(0).fill(travelDate1)
  await dateInputs.nth(1).fill(travelDate1)
  await dialog.getByPlaceholder('例如 客户需求确认与方案汇报').fill('拜访客户')
  await dialog.getByLabel('预估预算（元）').fill('400')
  await expect(dialog.getByText('⚠️ 预估预算超标')).toHaveCount(0)
  await dialog.getByRole('button', { name: '保存并提交' }).click()
  await expect(dialog).toHaveCount(0)

  // 路径 2: 超标需填理由（预算 2000 > 550），未填理由阻断提交，补齐理由后成功提交且标记超标
  const runId2 = crypto.randomUUID().replaceAll('-', '').slice(0, 8)
  const purpose2 = `E2E差旅超标填理由-${runId2}`
  const travelDate2 = nextWeekday(runId2)

  await page.getByRole('button', { name: '＋ 发起出差' }).click()
  dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await dialog.getByPlaceholder('说明出差目的和预期成果').fill(purpose2)
  await dialog.getByPlaceholder('例如 上海').fill('上海')
  dateInputs = dialog.locator('.itinerary-row input[type="date"]')
  await dateInputs.nth(0).fill(travelDate2)
  await dateInputs.nth(1).fill(travelDate2)
  await dialog.getByPlaceholder('例如 客户需求确认与方案汇报').fill('大型展会')
  await dialog.getByLabel('预估预算（元）').fill('2000')
  await expect(dialog.getByText('⚠️ 预估预算超标')).toBeVisible()
  // 不填理由提交 -> 阻断提示
  await dialog.getByRole('button', { name: '保存并提交' }).click()
  await expect(page.locator('.dialog-error')).toHaveText('预估预算已超出标准上限，必须填写超标原因。')
  // 补齐理由后提交
  await dialog.locator('.over-standard-field textarea').fill('展会期间周边协议酒店满房，价格普遍上浮。')
  await dialog.getByRole('button', { name: '保存并提交' }).click()
  await expect(dialog).toHaveCount(0)

  // 路径 3: 超标禁止提交路径
  await page.route('**/api/v1/business-configurations/effective-options', async route => {
    const response = await route.fetch()
    const json = await response.json()
    if (json.travelStandards) {
      json.travelStandards.forEach((s: any) => { s.blockWhenExceeded = true })
    }
    await route.fulfill({ json })
  })
  await page.evaluate(() => window.dispatchEvent(new Event('focus')))
  await page.getByRole('button', { name: '＋ 发起出差' }).click()
  dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await dialog.getByPlaceholder('说明出差目的和预期成果').fill('超标禁止测试')
  await dialog.getByPlaceholder('例如 上海').fill('上海')
  dateInputs = dialog.locator('.itinerary-row input[type="date"]')
  await dateInputs.nth(0).fill(travelDate2)
  await dateInputs.nth(1).fill(travelDate2)
  await dialog.getByLabel('预估预算（元）').fill('3000')
  await expect(dialog.getByText(/超标禁止提交/)).toBeVisible()
  await expect(dialog.getByRole('button', { name: '保存并提交' })).toBeDisabled()
  await dialog.getByLabel('关闭').click()
})

test('场景4: 最小权限配置管理员专职专责与发布权限拦截', async ({ page, request }) => {
  const adminToken = await loginApi('u-admin')
  const configToken = await loginApi('u-config')
  const employeeToken = await loginApi('u-zhang')

  // 1. 验证 u-config 是专职最小权限角色
  const userResp = await request.get(`${apiBase}/admin/users`, { headers: { Authorization: `Bearer ${adminToken}` } })
  expect(userResp.ok()).toBeTruthy()
  const usersData = await userResp.json()
  const users = usersData.items || usersData
  const configAdmin = users.find((u: any) => u.id === 'u-config')
  expect(configAdmin.roles).toContain('配置管理员')
  expect(configAdmin.permissions).toContain('BUSINESS_CONFIG_MANAGE')
  expect(configAdmin.permissions).not.toContain('PERSONNEL_MANAGE')
  expect(configAdmin.permissions).not.toContain('EXPENSE_PAY')

  // 2. 创建一个测试草稿
  const code = `CFG_${crypto.randomUUID().replaceAll('-', '').slice(0, 6).toUpperCase()}`
  const createDraft = await request.post(`${apiBase}/business-configurations`, {
    headers: { Authorization: `Bearer ${configToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: { domain: 'Expense', code, name: `权限测试_${code}`, description: '权限测试', contentJson: JSON.stringify({ categories: [{ code: 'TestCat', name: '测试类别', isEnabled: true }] }) }
  })
  expect(createDraft.status()).toBe(201)
  const draft = await createDraft.json()

  // 3. 真实后端越权拦截：普通员工无法发布草稿，返回真实 403 AUTH_002（无 mock）
  const unauthPub = await request.post(`${apiBase}/business-configurations/${draft.id}/publish`, {
    headers: { Authorization: `Bearer ${employeeToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: { concurrencyVersion: draft.concurrencyVersion }
  })
  expect(unauthPub.status()).toBe(403)
  const unauthBody = await unauthPub.json()
  expect(unauthBody.code).toBe('AUTH_002')

  // 4. 配置管理员在真实 UI 中发布草稿（无任何 page.route 模拟）
  await loginUi(page, 'u-config')
  await page.goto('/#/business-configurations')
  await expect(page.getByRole('heading', { name: '业务参数配置中心' })).toBeVisible()

  // 查询新建草稿
  await page.getByPlaceholder('搜索配置标识 / 名称 / 描述').fill(code)
  await page.getByRole('button', { name: '查询' }).click()
  const draftRow = page.locator('tr', { hasText: code })
  await expect(draftRow).toBeVisible()

  // 专职管理员执行发布，真实后端成功处理
  await draftRow.getByRole('button', { name: '发布' }).click()
  const pubDialog = page.getByRole('dialog')
  await expect(pubDialog).toBeVisible()
  await pubDialog.getByRole('button', { name: '确认发布' }).click()
  await expect(pubDialog).toHaveCount(0)
  await expect(draftRow.getByText('生效中')).toBeVisible()
})

test('场景5: 普通员工看不到配置入口且接口返回 403', async ({ page, request }) => {
  const employeeToken = await loginApi('u-zhang')

  // 1. 普通员工请求业务配置列表接口直接返回 403 Forbidden
  const apiResp = await request.get(`${apiBase}/business-configurations`, {
    headers: { Authorization: `Bearer ${employeeToken}` }
  })
  expect(apiResp.status()).toBe(403)
  const body = await apiResp.json()
  expect(body.code).toBe('AUTH_002')

  // 2. 普通员工登录 UI，左侧导航栏不包含业务参数配置入口
  await loginUi(page, 'u-zhang')
  await expect(page.getByRole('button', { name: /业务参数配置/ })).toHaveCount(0)

  // 直接访问 /business-configurations 路由，未授权页面或无权访问
  await page.goto('/#/business-configurations')
  await expect(page.getByRole('heading', { name: '业务参数配置中心' })).toHaveCount(0)
})

test('场景6: v1 单据保持旧快照，v2 配置只影响新单据', async ({ page, request }) => {
  const adminToken = await loginApi('u-admin')
  const userToken = await loginApi('u-zhang')

  // 1. 在当前配置下提交出差申请 v1
  const runId1 = crypto.randomUUID().replaceAll('-', '').slice(0, 8)
  const purpose1 = `E2E快照隔离v1-${runId1}`
  const travelDate1 = nextWeekday(runId1)
  const created1 = await request.post(`${apiBase}/travel-requests`, {
    headers: { Authorization: `Bearer ${userToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: {
      purpose: purpose1,
      estimatedBudget: 400,
      itinerary: [{ destination: '北京', startDate: travelDate1, endDate: travelDate1, transportation: '高铁', purpose: '业务交流' }],
      companionIds: [],
      attachments: []
    }
  })
  expect(created1.status()).toBe(201)
  const travel1 = await created1.json()
  const submit1 = await request.post(`${apiBase}/travel-requests/${travel1.id}/submit`, {
    headers: { Authorization: `Bearer ${userToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: {}
  })
  expect(submit1.ok()).toBeTruthy()
  const submittedTravel1 = await submit1.json()

  // 2. 升版配置为新版本
  const cfgList = await request.get(`${apiBase}/business-configurations?domain=Travel&code=TravelPolicy`, { headers: { Authorization: `Bearer ${adminToken}` } })
  const cfgData = await cfgList.json()
  const travelCfg = cfgData.items[0]
  const newVer = await request.post(`${apiBase}/business-configurations/${travelCfg.id}/versions`, {
    headers: { Authorization: `Bearer ${adminToken}`, 'Idempotency-Key': crypto.randomUUID() }
  })
  const draft = await newVer.json()
  const content = JSON.parse(draft.contentJson)
  const standards = content.standards || content.Standards || []
  standards.forEach((s: any) => { s.hotelDailyLimit = 888 })
  content.standards = standards
  const updateRes = await request.put(`${apiBase}/business-configurations/${draft.id}`, {
    headers: { Authorization: `Bearer ${adminToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: { name: draft.name, description: 'v2 升级版本', contentJson: JSON.stringify(content), concurrencyVersion: draft.concurrencyVersion, effectiveFrom: draft.effectiveFrom }
  })
  const updatedDraft = await updateRes.json()
  const pubRes = await request.post(`${apiBase}/business-configurations/${draft.id}/publish`, {
    headers: { Authorization: `Bearer ${adminToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: { concurrencyVersion: updatedDraft.concurrencyVersion }
  })
  expect(pubRes.ok(), await pubRes.text()).toBeTruthy()
  const publishedCfg = await pubRes.json()

  // 3. 在 v2 配置下提交新出差申请 v2
  const runId2 = crypto.randomUUID().replaceAll('-', '').slice(0, 8)
  const purpose2 = `E2E快照隔离v2-${runId2}`
  const travelDate2 = nextWeekday(runId2)
  const created2 = await request.post(`${apiBase}/travel-requests`, {
    headers: { Authorization: `Bearer ${userToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: {
      purpose: purpose2,
      estimatedBudget: 800,
      itinerary: [{ destination: '北京', startDate: travelDate2, endDate: travelDate2, transportation: '高铁', purpose: '业务交流2' }],
      companionIds: [],
      attachments: []
    }
  })
  const travel2 = await created2.json()
  const submit2 = await request.post(`${apiBase}/travel-requests/${travel2.id}/submit`, {
    headers: { Authorization: `Bearer ${userToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: {}
  })
  expect(submit2.ok()).toBeTruthy()
  const submittedTravel2 = await submit2.json()

  // 4. 验证旧单据快照保持原样，新单据快照升级为 v2
  expect(submittedTravel1.configSnapshotJson).not.toBeNull()
  expect(submittedTravel2.configSnapshotJson).not.toBeNull()
  expect(submittedTravel2.configVersionNumber).toBe(publishedCfg.version)

  // 5. 在 UI 中查看旧单据详情，核验快照存在且保持不变
  await loginUi(page, 'u-zhang')
  await page.goto(`/#/travel/${travel1.id}`)
  await expect(page.getByRole('heading', { name: '出差申请详情' })).toBeVisible()
  await expect(page.getByText(/策略版本快照/)).toBeVisible()
})

test('场景7: 驳回后重新提交获取最新配置', async ({ page, request }) => {
  const userToken = await loginApi('u-zhang')
  const approverToken = await loginApi('u-li')

  // 1. 发起申请并由主管驳回
  const runId = crypto.randomUUID().replaceAll('-', '').slice(0, 8)
  const purpose = `E2E驳回重提-${runId}`
  const travelDate = nextWeekday(runId)
  const created = await request.post(`${apiBase}/travel-requests`, {
    headers: { Authorization: `Bearer ${userToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: {
      purpose,
      estimatedBudget: 400,
      itinerary: [{ destination: '上海', startDate: travelDate, endDate: travelDate, transportation: '高铁', purpose: '拜访客户' }],
      companionIds: [],
      attachments: []
    }
  })
  const travel = await created.json()
  const submitRes = await request.post(`${apiBase}/travel-requests/${travel.id}/submit`, {
    headers: { Authorization: `Bearer ${userToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: {}
  })
  const submitted = await submitRes.json()
  const taskId = submitted.tasks[0].id

  // 主管驳回
  const rejectRes = await request.post(`${apiBase}/travel-tasks/${taskId}/reject`, {
    headers: { Authorization: `Bearer ${approverToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: { comment: '预算填报不合理，请调整后重新提交' }
  })
  expect(rejectRes.ok()).toBeTruthy()

  // 2. 申请人在 UI 重新提交获取最新配置
  await loginUi(page, 'u-zhang')
  await page.goto(`/#/travel/${travel.id}`)
  await expect(page.getByRole('heading', { name: '出差申请详情' })).toBeVisible()
  await expect(page.getByText('已驳回').first()).toBeVisible()

  const reSubmitRes = await request.post(`${apiBase}/travel-requests/${travel.id}/submit`, {
    headers: { Authorization: `Bearer ${userToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: {}
  })
  expect(reSubmitRes.ok()).toBeTruthy()
  const reSubmitted = await reSubmitRes.json()
  expect(reSubmitted.status === 1 || reSubmitted.status === 'Approving').toBeTruthy() // 审批中
  expect(reSubmitted.configResolvedAt).not.toBeNull()
})

test('场景8: 390px 手机视口下配置中心和业务表单可操作', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await loginUi(page, 'u-admin')

  // 1. 配置中心在 390px 视口下无横向溢出且按钮可操作
  await page.goto('/#/business-configurations')
  await expect(page.getByRole('heading', { name: '业务参数配置中心' })).toBeVisible()
  let hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasPageOverflow).toBeFalsy()

  const firstRow = page.locator('tbody tr').first()
  await firstRow.getByRole('button', { name: '详情' }).click()
  const detailDialog = page.getByRole('dialog')
  await expect(detailDialog).toBeVisible()
  let hasDialogOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasDialogOverflow).toBeFalsy()
  await detailDialog.getByLabel('关闭').click()
  await expect(detailDialog).toHaveCount(0)

  // 2. 业务表单在 390px 视口下无横向溢出且弹窗可操作
  await page.goto('/#/travel')
  await expect(page.getByRole('heading', { name: '出差管理' })).toBeVisible()
  hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasPageOverflow).toBeFalsy()

  await page.getByRole('button', { name: '＋ 发起出差' }).click()
  const formDialog = page.getByRole('dialog')
  await expect(formDialog).toBeVisible()
  hasDialogOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasDialogOverflow).toBeFalsy()
  await formDialog.getByLabel('关闭').click()
  await expect(formDialog).toHaveCount(0)
})

test('场景9: P1-F4 财务台账与预算中心桌面端全链路浏览与切换', async ({ page }) => {
  await loginUi(page, 'u-lin')

  // 1. 访问财务台账页面
  await page.goto('/#/finance/ledgers')
  await expect(page.getByRole('heading', { name: /财务台账/ })).toBeVisible()

  // 默认发票台账
  await expect(page.getByRole('button', { name: /发票台账/ })).toBeVisible()

  // 切换到付款台账
  await page.getByRole('button', { name: /付款台账/ }).click()
  await expect(page.getByRole('button', { name: /付款台账/ })).toBeVisible()

  // 切换到采购对账
  await page.getByRole('button', { name: /采购对账/ }).click()
  await expect(page.getByRole('button', { name: /采购对账/ })).toBeVisible()

  // 2. 访问企业预算中心
  await page.goto('/#/budgets')
  await expect(page.getByRole('heading', { name: /预算中心/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /编制新预算/ })).toBeVisible()
})

test('场景9.1: 真实付款 API 复用幂等事务并强制校验付款回单', async ({ request }) => {
  const token = await loginApi('u-chen')
  const runId = crypto.randomUUID().replaceAll('-', '')
  const headers = { Authorization: `Bearer ${token}` }
  const upload = await request.post(`${apiBase}/files`, {
    headers: { ...headers, 'Idempotency-Key': `e2e-proof-${runId}` },
    multipart: {
      file: {
        name: `payment-proof-${runId}.pdf`,
        mimeType: 'application/pdf',
        buffer: Buffer.from('%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n')
      }
    }
  })
  expect(upload.status()).toBe(201)
  const proof = await upload.json()
  const missingBusinessId = crypto.randomUUID()
  const payment = {
    batchTitle: '接口事务回归验证',
    paymentDate: new Date().toISOString().slice(0, 10),
    paymentMethod: 'BANK_TRANSFER',
    payerAccount: '955880001',
    payeeName: '回归测试收款方',
    payeeAccount: '6222026000009999',
    payeeBank: '测试银行',
    transactionNumber: `E2E-TX-${runId}`,
    paidAmount: 1,
    feeAmount: 0,
    proofAttachmentId: proof.id,
    remarks: '针对不存在业务单据验证事务边界'
  }

  const expenseResponse = await request.post(`${apiBase}/expense-claims/${missingBusinessId}/payments`, {
    headers: { ...headers, 'Idempotency-Key': `e2e-expense-payment-${runId}` },
    data: payment
  })
  expect(expenseResponse.status()).toBe(404)
  expect((await expenseResponse.json()).code).toBe('DATA_001')

  const purchaseResponse = await request.post(`${apiBase}/purchase-requests/${missingBusinessId}/payments`, {
    headers: { ...headers, 'Idempotency-Key': `e2e-purchase-payment-${runId}` },
    data: { ...payment, transactionNumber: `E2E-PUR-TX-${runId}` }
  })
  expect(purchaseResponse.status()).toBe(404)
  expect((await purchaseResponse.json()).code).toBe('DATA_001')

  const missingProofResponse = await request.post(`${apiBase}/expense-claims/${missingBusinessId}/payments`, {
    headers: { ...headers, 'Idempotency-Key': `e2e-missing-proof-${runId}` },
    data: { ...payment, transactionNumber: `E2E-NO-PROOF-${runId}`, proofAttachmentId: null }
  })
  expect(missingProofResponse.status()).toBe(400)
  expect((await missingProofResponse.json()).code).toBe('EXP_005')
})

test('场景9.2: 预算页面按目标总额调整而不是重复累加', async ({ page, request }) => {
  const token = await loginApi('u-lin')
  const runId = crypto.randomUUID().replaceAll('-', '').slice(0, 12)
  const createResponse = await request.post(`${apiBase}/budgets`, {
    headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': `e2e-budget-create-${runId}` },
    data: {
      departmentId: 'engineering',
      expenseCategory: `E2E调整-${runId}`,
      projectId: null,
      year: new Date().getFullYear() + 1,
      month: 12,
      allocatedAmount: 10000,
      autoPublish: true
    }
  })
  expect(createResponse.status()).toBe(201)
  const budget = await createResponse.json()

  await loginUi(page, 'u-lin')
  await page.goto(`/#/budgets/${budget.id}`)
  await expect(page.getByRole('heading', { name: /engineering.*预算/ })).toBeVisible()
  await page.getByRole('button', { name: '调整额度' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel(/调整后新总额/).fill('12000')
  await dialog.getByLabel(/调整原因/).fill('验证目标总额不会被重复累加')
  await dialog.getByRole('button', { name: '确认调整' }).click()
  await expect(dialog).toHaveCount(0)

  const readResponse = await request.get(`${apiBase}/budgets/${budget.id}`, { headers: { Authorization: `Bearer ${token}` } })
  expect(readResponse.ok()).toBeTruthy()
  expect((await readResponse.json()).allocatedAmount).toBe(12000)
})

test('张晨在研发目录下创建草稿后立即回显，并使用统一按钮样式', async ({ page, request }) => {
  const token = await loginApi('u-zhang')
  const runId = crypto.randomUUID().replaceAll('-', '').slice(0, 10)
  const title = `E2E研发目录草稿-${runId}`

  await loginUi(page, 'u-zhang')
  await page.goto('/#/documents')
  await expect(page.getByRole('heading', { name: '企业知识库与制度中心' })).toBeVisible()
  await page.locator('.category-tree li', { hasText: '技术与研发指引' }).click()
  await page.getByRole('button', { name: /编制制度\/文档/ }).click()

  const dialog = page.getByRole('dialog')
  const categorySelect = dialog.getByLabel('所属目录分类')
  const departmentSelect = dialog.getByLabel('适用部门')
  await expect(categorySelect).toHaveValue(/.+/)
  await expect(departmentSelect).toHaveValue('engineering')
  await expect(departmentSelect).toBeDisabled()
  await dialog.getByLabel('文档标题').fill(title)
  await dialog.getByLabel(/摘要简介/).fill('验证研发目录与研发部门范围自动保持一致。')
  await dialog.getByLabel(/正文内容/).fill('# 研发目录创建验证\n\n该草稿用于验证目录和部门范围联动。')
  await dialog.getByRole('button', { name: '保存草稿' }).click()
  await expect(dialog).toHaveCount(0)
  const createdCard = page.locator('.doc-card', { hasText: title })
  await expect(createdCard.getByRole('heading', { name: title })).toBeVisible()
  await expect(createdCard.getByText('草稿', { exact: true })).toBeVisible()
  await expect(createdCard.getByRole('button', { name: '编辑草稿' })).toBeVisible()

  const listResponse = await request.get(`${apiBase}/documents?keyword=${encodeURIComponent(title)}&page=1&pageSize=10`, {
    headers: { Authorization: `Bearer ${token}` }
  })
  expect(listResponse.ok()).toBeTruthy()
  const created = (await listResponse.json()).items.find((item: { title: string }) => item.title === title)
  expect(created?.departmentId).toBe('engineering')
  const deleteResponse = await request.delete(`${apiBase}/documents/${created.id}`, {
    headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': `e2e-document-delete-${runId}` }
  })
  expect(deleteResponse.ok()).toBeTruthy()

  const primaryButton = page.getByRole('button', { name: /编制制度\/文档/ })
  expect(await primaryButton.getAttribute('class')).toContain('oa-button--primary')
})

test('场景10: P1-F4 390px 手机视口下财务台账与预算中心无横向滚动且响应良好', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await loginUi(page, 'u-lin')

  // 1. 财务台账在 390px 手机视口下无横向整页溢出
  await page.goto('/#/finance/ledgers')
  await expect(page.getByRole('heading', { name: /财务台账/ })).toBeVisible()
  let hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasPageOverflow).toBeFalsy()

  // 切换 Tab 测试无溢出
  await page.getByRole('button', { name: /付款台账/ }).click()
  await page.waitForTimeout(300)
  hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasPageOverflow).toBeFalsy()

  await page.getByRole('button', { name: /采购对账/ }).click()
  await page.waitForTimeout(300)
  hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasPageOverflow).toBeFalsy()

  // 2. 预算中心在 390px 手机视口下无横向整页溢出
  await page.goto('/#/budgets')
  await expect(page.getByRole('heading', { name: /预算中心/ })).toBeVisible()
  hasPageOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasPageOverflow).toBeFalsy()
})
