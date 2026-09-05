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
