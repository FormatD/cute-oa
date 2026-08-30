const statusLabels: Record<string, string> = {
  Draft: '草稿', Approving: '审批中', Rejected: '已驳回', Approved: '已批准', Completed: '已完成', Withdrawn: '已撤回',
  Pending: '待处理', Cancelled: '已取消'
}

export const businessStatusLabel = (status: string | number) => statusLabels[String(status)] ?? String(status)
