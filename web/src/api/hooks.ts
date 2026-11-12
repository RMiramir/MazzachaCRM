import { notifications } from '@mantine/notifications';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './client';

/**
 * Действие, меняющее данные. После успеха все загруженные данные обновляются:
 * программа локальная, запросы мгновенные, а цифры на всех экранах связаны между собой.
 */
export function useAction<TArgs, TResult>(fn: (args: TArgs) => Promise<TResult>, successMessage?: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: async () => {
      await queryClient.invalidateQueries();
      if (successMessage) notifications.show({ message: successMessage, color: 'teal' });
    },
    onError: (error: Error) => {
      notifications.show({ title: 'Не получилось', message: error.message, color: 'red', autoClose: 8000 });
    },
  });
}

export const useSettings = () => useQuery({ queryKey: ['settings'], queryFn: api.settings.get });

/** Название валюты для подписей. */
export function useCurrency(): string {
  return useSettings().data?.currency ?? 'сомони';
}

export const useIngredients = () => useQuery({ queryKey: ['ingredients'], queryFn: api.ingredients.list });

export const useProducts = (includeArchived = false) =>
  useQuery({ queryKey: ['products', includeArchived], queryFn: () => api.products.list(includeArchived) });

export const useProduct = (id: string | null) =>
  useQuery({ queryKey: ['product', id], queryFn: () => api.products.get(id!), enabled: id !== null });

export const useDay = (date: string) => useQuery({ queryKey: ['day', date], queryFn: () => api.days.get(date) });

export const useStockMovements = (params: { ingredientId?: string | null; from?: string | null; to?: string | null }, enabled = true) =>
  useQuery({ queryKey: ['movements', params], queryFn: () => api.stock.movements(params), enabled });

export const useCustomers = (search = '') =>
  useQuery({ queryKey: ['customers', search], queryFn: () => api.customers.list(search) });

export const useOrders = (params: { activeOnly?: boolean; customerId?: string | null }, enabled = true) =>
  useQuery({ queryKey: ['orders', params], queryFn: () => api.orders.list(params), enabled });

export const usePeriodReport = (from: string | null, to: string | null) =>
  useQuery({
    queryKey: ['period', from, to],
    queryFn: () => api.reports.period(from!, to!),
    enabled: from !== null && to !== null,
  });

export const useMargins = () => useQuery({ queryKey: ['margins'], queryFn: api.reports.margins });
