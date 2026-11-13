// Типы ответов API. Имена полей совпадают с DTO слоя приложения (camelCase).

export type MeasureUnit = 'Gram' | 'Milliliter' | 'Piece';
export type IngredientKind = 'Ingredient' | 'Packaging';
export type ProductType = 'Finished' | 'SemiFinished';
export type OrderStatus = 'New' | 'InProgress' | 'Ready' | 'Delivered' | 'Cancelled';
export type OrderScope = 'Active' | 'Unpaid' | 'All';
export type CostMode = 'Recipe' | 'Manual';
export type StockMovementType = 'Purchase' | 'Production' | 'Adjustment' | 'WriteOff';

export interface Settings {
  hourlyRate: number;
  currency: string;
  monthlyProfitGoal: number | null;
  databasePath: string;
}

export interface Cost {
  ingredients: number;
  packaging: number;
  labor: number;
  total: number;
}

export interface Ingredient {
  id: string;
  name: string;
  kind: IngredientKind;
  unit: MeasureUnit;
  packagePrice: number;
  packageSize: number;
  unitPrice: number;
  stockQuantity: number;
  minStock: number | null;
  isLowStock: boolean;
  isOutOfStock: boolean;
  stockValue: number;
  usedInProducts: number;
}

export interface IngredientInput {
  name: string;
  kind: IngredientKind;
  unit: MeasureUnit;
  packagePrice: number;
  packageSize: number;
  minStock: number | null;
  initialStock?: number | null;
}

export interface StockMovement {
  id: string;
  date: string;
  ingredientId: string;
  ingredientName: string;
  unit: MeasureUnit;
  type: StockMovementType;
  quantity: number;
  totalCost: number | null;
  note: string | null;
  canDelete: boolean;
}

export interface StockMovements {
  items: StockMovement[];
  purchasesTotal: number;
}

export interface ProductListItem {
  id: string;
  name: string;
  type: ProductType;
  yieldQuantity: number;
  yieldUnit: MeasureUnit;
  laborMinutes: number;
  salePrice: number;
  isArchived: boolean;
  costMode: CostMode;
  unitCost: Cost | null;
  batchCost: number | null;
  profitPerUnit: number | null;
  markupPercent: number | null;
  costError: string | null;
}

export interface RecipeLine {
  ingredientId: string | null;
  componentProductId: string | null;
  name: string;
  unit: MeasureUnit;
  isPackaging: boolean;
  quantity: number;
  cost: number;
}

export interface ProductDetail {
  id: string;
  name: string;
  type: ProductType;
  yieldQuantity: number;
  yieldUnit: MeasureUnit;
  laborMinutes: number;
  salePrice: number;
  note: string | null;
  isArchived: boolean;
  costMode: CostMode;
  manualUnitCost: number;
  lines: RecipeLine[];
  batchCost: Cost;
  unitCost: Cost;
}

export interface RecipeLineInput {
  ingredientId: string | null;
  componentProductId: string | null;
  quantity: number;
}

export interface ProductInput {
  name: string;
  type: ProductType;
  yieldQuantity: number;
  yieldUnit: MeasureUnit;
  laborMinutes: number;
  salePrice: number;
  note: string | null;
  lines: RecipeLineInput[];
  costMode: CostMode;
  manualUnitCost: number;
}

export interface CostPreview {
  lineCosts: number[];
  batchCost: Cost;
  unitCost: Cost;
  profitPerUnit: number | null;
  markupPercent: number | null;
  breakEvenUnits: number | null;
}

export interface SaleEntry {
  quantity: number;
  unitPrice: number;
}

export interface DayBatch {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  unitCost: number;
  unitCostBreakdown: Cost;
  unitPrice: number;
  sold: number;
  writtenOff: number;
  remaining: number;
  totalCost: number;
  potentialRevenue: number;
  potentialProfit: number;
  actualRevenue: number;
  profitOnSold: number;
  sales: SaleEntry[];
  writeOffReason: string | null;
  soldLater: number;
  writtenOffLater: number;
  breakEvenUnits: number | null;
  orderId: string | null;
  orderNumber: number | null;
  orderIsConsignment: boolean;
  orderReturned: number;
}

export interface DayCarryOver {
  id: string;
  productId: string;
  productName: string;
  productionDate: string;
  unitCost: number;
  unitPrice: number;
  remainingAtStart: number;
  sold: number;
  writtenOff: number;
  remaining: number;
  revenue: number;
  profitOnSold: number;
  sales: SaleEntry[];
  writeOffReason: string | null;
  orderId: string | null;
  orderNumber: number | null;
  orderIsConsignment: boolean;
}

export interface DaySummary {
  productionCost: number;
  laborCost: number;
  expenses: number;
  totalCosts: number;
  revenue: number;
  profit: number;
  potentialRevenue: number;
  potentialProfit: number;
  profitOnSold: number;
  writeOffLoss: number;
  unsoldValueAtPrice: number;
  unsoldValueAtCost: number;
  received: number;
  receivedFromOrders: number;
  debtsTotal: number;
  actualProfit: number;
  dayOrdersBalance: number;
}

export interface Expense {
  id: string;
  date: string;
  name: string;
  amount: number;
}

export interface Day {
  date: string;
  summary: DaySummary;
  batches: DayBatch[];
  carryOver: DayCarryOver[];
  expenses: Expense[];
  orders: Order[];
}

export interface Customer {
  id: string;
  name: string;
  phone: string | null;
  note: string | null;
  isConsignment: boolean;
  ordersCount: number;
  ordersTotal: number;
  debt: number;
  lastOrderDate: string | null;
}

export interface CustomerInput {
  name: string;
  phone: string | null;
  note: string | null;
  isConsignment: boolean;
}

export interface OrderItem {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  returned: number;
  returnedWrittenOff: number;
  sold: number;
  unitPrice: number;
  amount: number;
  note: string | null;
}

export interface OrderPayment {
  id: string;
  date: string;
  amount: number;
  note: string | null;
}

export interface Order {
  id: string;
  number: number;
  customerId: string;
  customerName: string;
  customerPhone: string | null;
  isConsignment: boolean;
  dueDate: string;
  dueTime: string | null;
  status: OrderStatus;
  total: number;
  paid: number;
  balance: number;
  isPaid: boolean;
  note: string | null;
  producedDate: string | null;
  deliveredDate: string | null;
  reportedDate: string | null;
  items: OrderItem[];
  payments: OrderPayment[];
  itemsSummary: string;
  cost: number | null;
  profit: number | null;
}

export interface OrderItemInput {
  productId: string;
  quantity: number;
  unitPrice: number;
  note: string | null;
}

export interface OrderInput {
  customerId: string | null;
  newCustomerName: string | null;
  newCustomerPhone: string | null;
  dueDate: string;
  dueTime: string | null;
  prepayment: number;
  note: string | null;
  items: OrderItemInput[];
}

export interface PeriodDay {
  date: string;
  productionCost: number;
  expenses: number;
  totalCosts: number;
  revenue: number;
  profit: number;
  writeOffLoss: number;
  received: number;
}

export interface PeriodTotals {
  productionCost: number;
  laborCost: number;
  expenses: number;
  totalCosts: number;
  revenue: number;
  profit: number;
  profitOnSold: number;
  writeOffLoss: number;
  unsoldValueAtPrice: number;
  unsoldValueAtCost: number;
  potentialProfit: number;
  received: number;
}

export interface PeriodProduct {
  productId: string;
  name: string;
  made: number;
  sold: number;
  writtenOff: number;
  revenue: number;
  costOfSold: number;
  profitOnSold: number;
}

export interface PeriodReport {
  from: string;
  to: string;
  days: PeriodDay[];
  totals: PeriodTotals;
  products: PeriodProduct[];
}

export interface MarginRow {
  productId: string;
  name: string;
  unitCost: number;
  unitCostBreakdown: Cost;
  salePrice: number;
  profitPerUnit: number;
  markupPercent: number | null;
  marginPercent: number | null;
  yieldQuantity: number;
  batchCost: number;
  breakEvenUnits: number | null;
}

export interface OverviewPeriod {
  title: string;
  from: string;
  to: string;
  revenue: number;
  profit: number;
  received: number;
  previousRevenue: number;
  previousProfit: number;
  revenueChangePercent: number | null;
  profitChangePercent: number | null;
}

export interface OverviewGoal {
  goal: number;
  achieved: number;
  percent: number;
  remaining: number;
  daysLeft: number;
  perDayNeeded: number | null;
}

export interface Overview {
  today: string;
  periods: OverviewPeriod[];
  chart: PeriodDay[];
  goal: OverviewGoal | null;
  upcomingOrders: Order[];
  debtsTotal: number;
  debtorsCount: number;
  lowStockCount: number;
}
