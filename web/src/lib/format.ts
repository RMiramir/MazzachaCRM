import dayjs from 'dayjs';
import 'dayjs/locale/ru';
import type { IngredientKind, MeasureUnit, OrderStatus, StockMovementType } from '../api/types';

dayjs.locale('ru');

const moneyFormat = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 });
const quantityFormat = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 3 });
const percentFormat = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 });

const withMinus = (text: string) => text.replace('-', '−');

/** Сумма: до двух знаков после запятой, без лишних нулей (7,5; 210,75; 1 254). */
export const money = (value: number | null | undefined) =>
  value === null || value === undefined ? '—' : withMinus(moneyFormat.format(value));

/** Сумма со знаком: +72; −341,5. */
export const signedMoney = (value: number | null | undefined) =>
  value === null || value === undefined ? '—' : (value > 0 ? '+' : '') + money(value);

export const quantity = (value: number | null | undefined) =>
  value === null || value === undefined ? '—' : withMinus(quantityFormat.format(value));

export const percent = (value: number | null | undefined) =>
  value === null || value === undefined ? '—' : `${withMinus(percentFormat.format(value))}%`;

export const unitLabel: Record<MeasureUnit, string> = { Gram: 'г', Milliliter: 'мл', Piece: 'шт' };

export const kindLabel: Record<IngredientKind, string> = { Ingredient: 'Продукт', Packaging: 'Упаковка' };

export const orderStatusLabel: Record<OrderStatus, string> = {
  New: 'Новый',
  InProgress: 'Готовится',
  Ready: 'Готов',
  Delivered: 'Выдан',
  Cancelled: 'Отменён',
};

export const orderStatusColor: Record<OrderStatus, string> = {
  New: 'blue',
  InProgress: 'orange',
  Ready: 'teal',
  Delivered: 'gray',
  Cancelled: 'red',
};

export const movementLabel: Record<StockMovementType, string> = {
  Purchase: 'Закупка',
  Production: 'Производство',
  Adjustment: 'Инвентаризация',
  WriteOff: 'Списание',
};

export const ISO = 'YYYY-MM-DD';

export const today = () => dayjs().format(ISO);

export const addDays = (date: string, days: number) => dayjs(date).add(days, 'day').format(ISO);

export const isIsoDate = (value: string | undefined): value is string =>
  !!value && /^\d{4}-\d{2}-\d{2}$/.test(value) && dayjs(value).isValid();

/** 12 ноября, чт */
export const longDate = (date: string) => dayjs(date).format('D MMMM, dd');

/** 12.11.2026 */
export const shortDate = (date: string | null | undefined) => (date ? dayjs(date).format('DD.MM.YYYY') : '—');

/** 15:00 из 15:00:00 */
export const shortTime = (time: string | null | undefined) => (time ? time.slice(0, 5) : '');

/** Значение поля ввода числа: пустое поле даёт null. */
export function toNumber(value: number | string | null | undefined): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (value === null || value === undefined || value.trim() === '') return null;
  const parsed = Number(value.replace(/\s/g, '').replace(',', '.'));
  return Number.isFinite(parsed) ? parsed : null;
}

/** Цвет суммы по знаку. */
export const signColor = (value: number) => (value > 0 ? 'teal.8' : value < 0 ? 'red.7' : undefined);
