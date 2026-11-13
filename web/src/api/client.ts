import type {
  CostPreview,
  Customer,
  CustomerInput,
  Day,
  Ingredient,
  IngredientInput,
  MarginRow,
  Order,
  OrderInput,
  OrderScope,
  OrderStatus,
  Overview,
  PeriodReport,
  ProductDetail,
  ProductInput,
  ProductListItem,
  RecipeLineInput,
  SaleEntry,
  Settings,
  StockMovements,
} from './types';

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public errors?: Record<string, string[]>,
  ) {
    super(message);
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`/api${url}`, {
      method,
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError('Нет связи с программой. Проверьте, что она запущена.', 0);
  }

  const text = await response.text();
  let data: any;
  try {
    data = text ? JSON.parse(text) : undefined;
  } catch {
    data = undefined;
  }

  if (!response.ok) {
    throw new ApiError(data?.detail ?? data?.title ?? 'Что-то пошло не так.', response.status, data?.errors);
  }
  return data as T;
}

const get = <T>(url: string) => request<T>('GET', url);
const post = <T>(url: string, body?: unknown) => request<T>('POST', url, body ?? {});
const put = <T>(url: string, body: unknown) => request<T>('PUT', url, body);
const del = <T>(url: string) => request<T>('DELETE', url);

function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') search.set(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}

type Created = { id: string };

export const api = {
  settings: {
    get: () => get<Settings>('/settings'),
    update: (body: { hourlyRate: number; currency: string; monthlyProfitGoal: number | null }) => put<void>('/settings', body),
  },

  ingredients: {
    list: () => get<Ingredient[]>('/ingredients'),
    create: (body: IngredientInput) => post<Created>('/ingredients', body),
    update: ({ id, ...body }: IngredientInput & { id: string }) => put<void>(`/ingredients/${id}`, body),
    remove: (id: string) => del<void>(`/ingredients/${id}`),
    purchase: ({ id, ...body }: { id: string; date: string; quantity: number; totalCost: number; updatePrice: boolean; note: string | null }) =>
      post<Created>(`/ingredients/${id}/purchases`, body),
    adjust: ({ id, ...body }: { id: string; date: string; actualQuantity: number; note: string | null }) =>
      post<Created>(`/ingredients/${id}/adjustments`, body),
    writeOff: ({ id, ...body }: { id: string; date: string; quantity: number; note: string | null }) =>
      post<Created>(`/ingredients/${id}/write-offs`, body),
  },

  stock: {
    movements: (params: { ingredientId?: string | null; from?: string | null; to?: string | null }) =>
      get<StockMovements>(`/stock/movements${query(params)}`),
    removeMovement: (id: string) => del<void>(`/stock/movements/${id}`),
  },

  products: {
    list: (includeArchived: boolean) => get<ProductListItem[]>(`/products${query({ includeArchived })}`),
    get: (id: string) => get<ProductDetail>(`/products/${id}`),
    create: (body: ProductInput) => post<Created>('/products', body),
    update: ({ id, ...body }: ProductInput & { id: string }) => put<void>(`/products/${id}`, body),
    remove: (id: string) => del<void>(`/products/${id}`),
    archive: ({ id, archived }: { id: string; archived: boolean }) => post<void>(`/products/${id}/archive`, { archived }),
    previewCost: (body: {
      productId: string | null;
      yieldQuantity: number;
      laborMinutes: number;
      salePrice: number;
      lines: RecipeLineInput[];
      costMode: 'Recipe' | 'Manual';
      manualUnitCost: number;
    }) => post<CostPreview>('/products/cost-preview', body),
  },

  days: {
    get: (date: string) => get<Day>(`/days/${date}`),
    copyFrom: ({ date, fromDate }: { date: string; fromDate: string }) =>
      post<{ copied: number }>(`/days/${date}/copy-from/${fromDate}`),
  },

  batches: {
    create: (body: { date: string; productId: string; quantity: number; unitPrice: number | null; soldQuantity: number | null }) =>
      post<Created>('/batches', body),
    update: ({ id, ...body }: { id: string; quantity: number; unitPrice: number }) => put<void>(`/batches/${id}`, body),
    remove: (id: string) => del<void>(`/batches/${id}`),
    recalculateCost: (id: string) => post<void>(`/batches/${id}/recalculate-cost`),
    setSales: ({ id, date, entries }: { id: string; date: string; entries: SaleEntry[] }) =>
      put<void>(`/batches/${id}/sales/${date}`, { entries }),
    setWriteOff: ({ id, date, quantity, reason }: { id: string; date: string; quantity: number; reason: string | null }) =>
      put<void>(`/batches/${id}/write-offs/${date}`, { quantity, reason }),
  },

  expenses: {
    create: (body: { date: string; name: string; amount: number }) => post<Created>('/expenses', body),
    remove: (id: string) => del<void>(`/expenses/${id}`),
  },

  customers: {
    list: (search?: string) => get<Customer[]>(`/customers${query({ search })}`),
    create: (body: CustomerInput) => post<Created>('/customers', body),
    update: ({ id, ...body }: CustomerInput & { id: string }) => put<void>(`/customers/${id}`, body),
    remove: (id: string) => del<void>(`/customers/${id}`),
  },

  orders: {
    list: (params: { scope?: OrderScope; from?: string | null; to?: string | null; customerId?: string | null }) =>
      get<Order[]>(`/orders${query(params)}`),
    create: (body: OrderInput) => post<Created>('/orders', body),
    update: ({ id, ...body }: Omit<OrderInput, 'newCustomerName' | 'newCustomerPhone' | 'customerId' | 'prepayment'> & { id: string; customerId: string }) =>
      put<void>(`/orders/${id}`, body),
    addPayment: ({ id, ...body }: { id: string; date: string; amount: number; note: string | null }) =>
      post<{ id: string }>(`/orders/${id}/payments`, body),
    removePayment: ({ id, paymentId }: { id: string; paymentId: string }) => del<void>(`/orders/${id}/payments/${paymentId}`),
    shopReport: ({ id, ...body }: { id: string; date: string; items: { itemId: string; sold: number }[] }) =>
      post<void>(`/orders/${id}/shop-report`, body),
    repeat: ({ id, dueDate }: { id: string; dueDate: string }) => post<{ id: string }>(`/orders/${id}/repeat`, { dueDate }),
    setStatus: ({ id, status, date }: { id: string; status: OrderStatus; date: string }) =>
      post<void>(`/orders/${id}/status`, { status, date }),
    remove: (id: string) => del<void>(`/orders/${id}`),
  },

  reports: {
    period: (from: string, to: string) => get<PeriodReport>(`/reports/period${query({ from, to })}`),
    margins: () => get<MarginRow[]>('/reports/margins'),
    overview: (today: string) => get<Overview>(`/reports/overview${query({ today })}`),
  },
};
