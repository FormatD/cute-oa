export const PAGE_SIZE = 8

export type PageResult<T> = {
  items: T[]
  currentPage: number
  totalPages: number
  total: number
}

export function paginate<T>(items: T[], page: number): PageResult<T> {
  const totalPages = Math.max(1, Math.ceil(items.length / PAGE_SIZE))
  const currentPage = Math.min(Math.max(page, 1), totalPages)
  const offset = (currentPage - 1) * PAGE_SIZE
  return { items: items.slice(offset, offset + PAGE_SIZE), currentPage, totalPages, total: items.length }
}
