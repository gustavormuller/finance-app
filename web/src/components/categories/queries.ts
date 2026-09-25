import { useQuery } from '@tanstack/react-query';

import { api } from '@/api/finance';

/** Under `['categories']`, so the invalidation after a category write refreshes every list and the usage. */
export function useCategories() {
  return useQuery({ queryKey: ['categories'], queryFn: api.listCategories });
}

export function useCategoryUsage() {
  return useQuery({ queryKey: ['categories', 'usage'], queryFn: api.categoryUsage });
}
