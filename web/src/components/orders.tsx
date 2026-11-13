import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Checkbox,
  Group,
  Menu,
  Modal,
  Paper,
  Select,
  Stack,
  Table,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core';
import { DatePickerInput, TimePicker } from '@mantine/dates';
import { notifications } from '@mantine/notifications';
import {
  IconArrowsExchange,
  IconBuildingStore,
  IconCash,
  IconCopy,
  IconDotsVertical,
  IconPencil,
  IconPlus,
  IconReceipt,
  IconTrash,
  IconX,
} from '@tabler/icons-react';
import { useState } from 'react';
import { api } from '../api/client';
import { useAction, useCurrency, useCustomers, useProducts } from '../api/hooks';
import type { Order, OrderStatus } from '../api/types';
import { addDays, money, orderStatusColor, orderStatusLabel, quantity, shortDate, shortTime, toNumber, today } from '../lib/format';
import { NumberField } from './inputs';
import { Profit, useConfirm } from './ui';

// ---------- Статус и следующий шаг ----------

type Step = { kind: 'status'; status: OrderStatus; label: string; done: string } | { kind: 'payment'; label: string };

/** Одна главная кнопка для заказа: что сделать дальше. */
export function nextStep(order: Order): Step | null {
  const shop = order.isConsignment;
  switch (order.status) {
    case 'New':
      return { kind: 'status', status: 'InProgress', label: 'В работу', done: 'Заказ взят в работу' };
    case 'InProgress':
      return {
        kind: 'status',
        status: 'Ready',
        label: 'Готов',
        done: 'Заказ готов: затраты и выручка записаны в этот день, сырьё списано',
      };
    case 'Ready':
      return {
        kind: 'status',
        status: 'Delivered',
        label: shop ? 'Отправлено' : 'Выдать',
        done: shop ? 'Поставка отправлена' : 'Заказ выдан',
      };
    case 'Delivered':
      return order.balance > 0 ? { kind: 'payment', label: 'Оплата' } : null;
    default:
      return null;
  }
}

const allowedTargets: Record<OrderStatus, OrderStatus[]> = {
  New: ['InProgress', 'Ready', 'Delivered', 'Cancelled'],
  InProgress: ['New', 'Ready', 'Delivered', 'Cancelled'],
  Ready: ['New', 'InProgress', 'Delivered', 'Cancelled'],
  Delivered: ['Ready'],
  Cancelled: ['New'],
};

/** Статус с учётом оплаты: «Выдан» превращается в «Оплачен» или «Ждём оплату». */
export function OrderStatusBadge({ order }: { order: Order }) {
  if (order.status === 'Delivered') {
    return order.isPaid ? (
      <Badge color="teal" variant="light">
        Оплачен
      </Badge>
    ) : (
      <Badge color="orange" variant="light">
        {order.isConsignment ? 'Отправлен, ждём оплату' : 'Выдан, ждём оплату'}
      </Badge>
    );
  }
  return (
    <Badge color={orderStatusColor[order.status]} variant="light">
      {order.status === 'Ready' && order.balance > 0 ? 'Готов, не оплачен' : orderStatusLabel[order.status]}
    </Badge>
  );
}

/** Остаток к оплате одной строкой. */
export function PaymentText({ order }: { order: Order }) {
  if (order.status === 'Cancelled') return null;
  if (order.balance > 0) {
    return (
      <Text size="sm">
        Оплачено {money(order.paid)} из {money(order.total)}, осталось{' '}
        <Text span fw={700} c="orange.8">
          {money(order.balance)}
        </Text>
      </Text>
    );
  }
  if (order.balance < 0) {
    return (
      <Text size="sm" c="dimmed">
        Переплата {money(-order.balance)}
      </Text>
    );
  }
  return (
    <Text size="sm" c="teal.8">
      Оплачено полностью
    </Text>
  );
}

export function ShopBadge() {
  return (
    <Badge size="sm" variant="light" color="grape" leftSection={<IconBuildingStore size={12} />}>
      Магазин
    </Badge>
  );
}

// ---------- Действия ----------

type Dialog =
  | { kind: 'edit' }
  | { kind: 'payment' }
  | { kind: 'report' }
  | { kind: 'repeat' }
  | { kind: 'status' };

/**
 * Главная кнопка следующего шага и меню остальных действий.
 * `actionDate` — день, в который записываются готовность, выдача и оплата.
 */
export function OrderActions({ order, actionDate, compact }: { order: Order; actionDate: string; compact?: boolean }) {
  const confirm = useConfirm();
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const setStatus = useAction(api.orders.setStatus);
  const remove = useAction(api.orders.remove, 'Заказ удалён');

  const step = nextStep(order);
  const produced = order.status === 'Ready' || order.status === 'Delivered';
  const active = order.status !== 'Delivered' && order.status !== 'Cancelled';

  const runStep = async () => {
    if (!step) return;
    if (step.kind === 'payment') {
      setDialog({ kind: 'payment' });
      return;
    }
    await setStatus.mutateAsync({ id: order.id, status: step.status, date: actionDate });
    notifications.show({ message: `№${order.number}. ${step.done}`, color: 'teal', autoClose: 3000 });
  };

  const cancel = async () => {
    const ok = await confirm({
      title: `Отменить заказ №${order.number}?`,
      message: produced
        ? 'Заказ уже изготовлен. Изделия останутся в таблице дня как обычный остаток: их можно продать или списать.'
        : 'Заказ останется в списке со статусом «Отменён».',
      confirmLabel: 'Отменить заказ',
      danger: true,
    });
    if (ok) setStatus.mutate({ id: order.id, status: 'Cancelled', date: actionDate });
  };

  const removeOrder = async () => {
    const ok = await confirm({
      title: `Удалить заказ №${order.number}?`,
      message: `${order.customerName}: ${order.itemsSummary}. Удаление нельзя отменить.`,
      confirmLabel: 'Удалить',
      danger: true,
    });
    if (ok) remove.mutate(order.id);
  };

  return (
    <>
      <Group gap={4} justify="flex-end" wrap="nowrap">
        {step && (
          <Button
            variant={step.kind === 'payment' ? 'filled' : 'light'}
            color={step.kind === 'payment' ? 'orange' : undefined}
            size={compact ? 'compact-sm' : 'sm'}
            leftSection={step.kind === 'payment' ? <IconCash size={16} /> : undefined}
            onClick={() => void runStep().catch(() => undefined)}
            loading={setStatus.isPending}
          >
            {step.label}
          </Button>
        )}
        <Menu position="bottom-end" withinPortal>
          <Menu.Target>
            <ActionIcon variant="subtle" color="gray" size={compact ? 'md' : 'lg'} aria-label={`Действия: заказ №${order.number}`}>
              <IconDotsVertical size={16} />
            </ActionIcon>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Item leftSection={<IconPencil size={16} />} onClick={() => setDialog({ kind: 'edit' })}>
              Изменить
            </Menu.Item>
            {order.status !== 'Cancelled' && (
              <Menu.Item leftSection={<IconCash size={16} />} onClick={() => setDialog({ kind: 'payment' })}>
                Оплаты
              </Menu.Item>
            )}
            {produced && (
              <Menu.Item leftSection={<IconReceipt size={16} />} onClick={() => setDialog({ kind: 'report' })}>
                {order.isConsignment ? 'Отчёт магазина: продано и возврат' : 'Возврат'}
              </Menu.Item>
            )}
            <Menu.Item leftSection={<IconCopy size={16} />} onClick={() => setDialog({ kind: 'repeat' })}>
              {order.isConsignment ? 'Повторить поставку' : 'Повторить заказ'}
            </Menu.Item>
            <Menu.Item leftSection={<IconArrowsExchange size={16} />} onClick={() => setDialog({ kind: 'status' })}>
              Сменить статус
            </Menu.Item>
            {active && (
              <Menu.Item leftSection={<IconX size={16} />} onClick={() => void cancel()}>
                Отменить заказ
              </Menu.Item>
            )}
            <Menu.Divider />
            <Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => void removeOrder()}>
              Удалить
            </Menu.Item>
          </Menu.Dropdown>
        </Menu>
      </Group>

      {dialog?.kind === 'edit' && <OrderEditor order={order} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'payment' && <PaymentModal order={order} date={actionDate} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'report' && <ShopReportModal order={order} date={actionDate} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'repeat' && <RepeatModal order={order} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'status' && (
        <StatusModal order={order} date={actionDate} onClose={() => setDialog(null)} />
      )}
    </>
  );
}

/** Карточка заказа для экрана «День». */
export function OrderCard({ order, actionDate }: { order: Order; actionDate: string }) {
  const done = order.status === 'Delivered' && order.isPaid;
  return (
    <Paper withBorder radius="md" p="sm" style={{ opacity: done ? 0.75 : 1 }}>
      <Group justify="space-between" align="flex-start" wrap="nowrap" gap="sm">
        <Stack gap={2} style={{ minWidth: 0 }}>
          <Group gap={8} wrap="wrap">
            <Text fw={600}>
              №{order.number} · {order.customerName}
            </Text>
            {order.isConsignment && <ShopBadge />}
            <OrderStatusBadge order={order} />
          </Group>
          <Text size="sm" c="dimmed">
            {order.dueTime ? `К ${shortTime(order.dueTime)} · ` : ''}
            {order.customerPhone ?? ''}
          </Text>
          <Text size="sm">{order.itemsSummary}</Text>
          {order.items.some((i) => i.returned > 0) && (
            <Text size="xs" c="grape.7">
              Возврат: {order.items.filter((i) => i.returned > 0).map((i) => `${i.productName} × ${quantity(i.returned)}`).join(', ')}
            </Text>
          )}
          {order.note && (
            <Text size="xs" c="dimmed">
              {order.note}
            </Text>
          )}
          <Group gap="md" mt={4}>
            <Text size="sm">
              Сумма <b>{money(order.total)}</b>
            </Text>
            <PaymentText order={order} />
            {order.profit !== null && order.status !== 'Cancelled' && (
              <Text size="xs" c="dimmed">
                прибыль <Profit value={order.profit} />
              </Text>
            )}
          </Group>
        </Stack>
        <OrderActions order={order} actionDate={actionDate} />
      </Group>
    </Paper>
  );
}

// ---------- Оплаты ----------

function PaymentModal({ order, date, onClose }: { order: Order; date: string; onClose: () => void }) {
  const currency = useCurrency();
  const add = useAction(api.orders.addPayment, 'Оплата записана');
  const remove = useAction(api.orders.removePayment);
  const [amount, setAmount] = useState<number | string>(order.balance > 0 ? order.balance : '');
  const [paymentDate, setPaymentDate] = useState<string>(date);
  const [note, setNote] = useState('');

  const amountNumber = toNumber(amount);
  const canSave = amountNumber !== null && amountNumber > 0;

  const save = async () => {
    if (!canSave) return;
    await add.mutateAsync({ id: order.id, date: paymentDate, amount: amountNumber, note: note.trim() || null });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={`Оплата: заказ №${order.number}, ${order.customerName}`} centered>
      <Stack gap="sm">
        <Group gap="lg">
          <Text size="sm">
            Сумма заказа <b>{money(order.total)}</b>
          </Text>
          <Text size="sm">Оплачено {money(order.paid)}</Text>
          <Text size="sm" fw={700} c={order.balance > 0 ? 'orange.8' : 'teal.8'}>
            {order.balance > 0 ? `Осталось ${money(order.balance)}` : 'Оплачено полностью'}
          </Text>
        </Group>

        {order.payments.length > 0 && (
          <Table verticalSpacing={4}>
            <Table.Tbody>
              {order.payments.map((payment) => (
                <Table.Tr key={payment.id}>
                  <Table.Td>{shortDate(payment.date)}</Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed">
                      {payment.note}
                    </Text>
                  </Table.Td>
                  <Table.Td className="num">{money(payment.amount)}</Table.Td>
                  <Table.Td w={40}>
                    <ActionIcon
                      variant="subtle"
                      color="gray"
                      aria-label="Удалить оплату"
                      onClick={() => remove.mutate({ id: order.id, paymentId: payment.id })}
                    >
                      <IconTrash size={16} />
                    </ActionIcon>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}

        <form
          onSubmit={(event) => {
            event.preventDefault();
            void save().catch(() => undefined);
          }}
        >
          <Stack gap="sm">
            <Group gap="sm" grow align="flex-end">
              <NumberField label={`Получено, ${currency}`} value={amount} onChange={setAmount} data-autofocus />
              <DatePickerInput label="Дата" value={paymentDate} onChange={(value) => value && setPaymentDate(value)} valueFormat="D MMMM YYYY" />
            </Group>
            <TextInput label="Комментарий" placeholder="Наличными, переводом…" value={note} onChange={(event) => setNote(event.currentTarget.value)} />
            <Group justify="flex-end" gap="sm">
              <Button variant="default" onClick={onClose}>
                Закрыть
              </Button>
              <Button type="submit" disabled={!canSave} loading={add.isPending}>
                Записать оплату
              </Button>
            </Group>
          </Stack>
        </form>
      </Stack>
    </Modal>
  );
}

// ---------- Отчёт магазина ----------

function ShopReportModal({ order, date, onClose }: { order: Order; date: string; onClose: () => void }) {
  const report = useAction(api.orders.shopReport, 'Отчёт записан');
  const [reportDate, setReportDate] = useState<string>(date < (order.producedDate ?? date) ? order.producedDate! : date);
  const [sold, setSold] = useState<Record<string, number | string>>(
    Object.fromEntries(order.items.map((item) => [item.id, item.sold])),
  );

  const rows = order.items.map((item) => {
    const soldNumber = toNumber(sold[item.id]) ?? 0;
    return { item, sold: soldNumber, returned: item.quantity - soldNumber, invalid: soldNumber > item.quantity };
  });
  const total = rows.reduce((sum, row) => sum + Math.max(row.sold, 0) * row.item.unitPrice, 0);
  const invalid = rows.some((row) => row.invalid);

  const save = async () => {
    await report.mutateAsync({
      id: order.id,
      date: reportDate,
      items: rows.map((row) => ({ itemId: row.item.id, sold: row.sold })),
    });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={`${order.isConsignment ? 'Отчёт магазина' : 'Возврат'}: №${order.number}, ${order.customerName}`} size="lg" centered>
      <Stack gap="sm">
        <Text size="sm" c="dimmed">
          Укажите, сколько продано. Остальное считается возвратом: выручка дня отправки ({shortDate(order.producedDate)})
          уменьшится, а вернувшиеся изделия появятся в «Остатках прошлых дней», где их можно продать или списать.
        </Text>
        <Table verticalSpacing={6}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Изделие</Table.Th>
              <Table.Th className="num">Отправлено</Table.Th>
              <Table.Th className="num">Продано</Table.Th>
              <Table.Th className="num">Вернули</Table.Th>
              <Table.Th className="num">К оплате</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map(({ item, sold: soldNumber, returned, invalid: rowInvalid }) => (
              <Table.Tr key={item.id}>
                <Table.Td>{item.productName}</Table.Td>
                <Table.Td className="num">{quantity(item.quantity)}</Table.Td>
                <Table.Td className="num">
                  <Group justify="flex-end">
                    <NumberField
                      aria-label={`Продано: ${item.productName}`}
                      size="xs"
                      w={80}
                      value={sold[item.id]}
                      onChange={(value) => setSold({ ...sold, [item.id]: value })}
                      error={rowInvalid}
                      decimalScale={3}
                    />
                  </Group>
                </Table.Td>
                <Table.Td className="num">
                  <Text span inherit c={returned > 0 ? 'grape.7' : 'dimmed'}>
                    {quantity(Math.max(returned, 0))}
                  </Text>
                </Table.Td>
                <Table.Td className="num">{money(Math.max(soldNumber, 0) * item.unitPrice)}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
        <Group justify="space-between" align="flex-end">
          <DatePickerInput label="Дата отчёта" value={reportDate} onChange={(value) => value && setReportDate(value)} valueFormat="D MMMM YYYY" w={200} />
          <Text>
            К оплате всего: <b>{money(total)}</b>
            {order.paid > 0 && `, уже оплачено ${money(order.paid)}`}
          </Text>
        </Group>
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose}>
            Отмена
          </Button>
          <Button onClick={() => void save().catch(() => undefined)} disabled={invalid} loading={report.isPending}>
            Сохранить отчёт
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

// ---------- Повтор ----------

function RepeatModal({ order, onClose }: { order: Order; onClose: () => void }) {
  const repeat = useAction(api.orders.repeat, order.isConsignment ? 'Новая поставка создана' : 'Новый заказ создан');
  const [dueDate, setDueDate] = useState<string>(addDays(today(), 1));

  const save = async () => {
    await repeat.mutateAsync({ id: order.id, dueDate });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={order.isConsignment ? 'Повторить поставку' : 'Повторить заказ'} centered size="sm">
      <Stack gap="sm">
        <Text size="sm">
          {order.customerName}: {order.itemsSummary}. Те же изделия, количество и цены.
        </Text>
        <DatePickerInput label="На какой день" value={dueDate} onChange={(value) => value && setDueDate(value)} valueFormat="D MMMM YYYY, dd" data-autofocus />
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose}>
            Отмена
          </Button>
          <Button onClick={() => void save().catch(() => undefined)} loading={repeat.isPending}>
            Создать
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

// ---------- Смена статуса ----------

function StatusModal({ order, date, onClose }: { order: Order; date: string; onClose: () => void }) {
  const setStatus = useAction(api.orders.setStatus, 'Статус изменён');
  const targets = allowedTargets[order.status];
  const [status, setStatusValue] = useState<OrderStatus>(targets[0]);
  const [statusDate, setStatusDate] = useState<string>(date);

  const produced = order.status === 'Ready' || order.status === 'Delivered';
  const needsDate = (status === 'Ready' || status === 'Delivered') && !produced;

  const explanation: Record<OrderStatus, string> = {
    New: produced ? 'Изделия заказа будут убраны из таблицы дня, сырьё вернётся на склад.' : 'Заказ вернётся в начало.',
    InProgress: produced
      ? 'Изделия заказа будут убраны из таблицы дня, сырьё вернётся на склад.'
      : 'Заказ отмечается как взятый в работу. На склад и на цифры дня это не влияет.',
    Ready:
      order.status === 'Delivered'
        ? 'Снимается только отметка о выдаче, цифры дня не меняются.'
        : 'Изделия попадут в таблицу выбранного дня: затраты и выручка записываются в этот день, сырьё списывается.',
    Delivered: produced
      ? 'Отметка о выдаче. Цифры дня не меняются.'
      : 'Заказ будет изготовлен и выдан в выбранный день: затраты и выручка записываются в этот день.',
    Cancelled: produced
      ? 'Изготовленные изделия останутся в таблице дня как обычный остаток: их можно продать или списать.'
      : 'Заказ останется в списке со статусом «Отменён».',
  };

  const save = async () => {
    await setStatus.mutateAsync({ id: order.id, status, date: statusDate });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={`Заказ №${order.number}: статус`} centered size="sm">
      <Stack gap="sm">
        <Text size="sm" c="dimmed">
          Сейчас: {orderStatusLabel[order.status]}
          {order.producedDate && `, готов ${shortDate(order.producedDate)}`}
        </Text>
        <Select
          label="Новый статус"
          data={targets.map((target) => ({ value: target, label: orderStatusLabel[target] }))}
          value={status}
          onChange={(value) => value && setStatusValue(value as OrderStatus)}
          allowDeselect={false}
        />
        {needsDate && (
          <DatePickerInput label="День готовности" value={statusDate} onChange={(value) => value && setStatusDate(value)} valueFormat="D MMMM YYYY" />
        )}
        <Text size="sm">{explanation[status]}</Text>
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose}>
            Отмена
          </Button>
          <Button onClick={() => void save().catch(() => undefined)} loading={setStatus.isPending}>
            Сохранить
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

// ---------- Форма заказа ----------

interface DraftItem {
  key: number;
  productId: string | null;
  quantity: number | string;
  unitPrice: number | string;
  note: string;
}

let nextItemKey = 1;
const emptyItem = (): DraftItem => ({ key: nextItemKey++, productId: null, quantity: 1, unitPrice: '', note: '' });

/** Новый заказ или изменение существующего. `defaultDate` подставляется в день выдачи нового заказа. */
export function OrderEditor({ order, defaultDate, onClose }: { order: Order | null; defaultDate?: string; onClose: () => void }) {
  const currency = useCurrency();
  const { data: customers } = useCustomers();
  const { data: products } = useProducts(false);
  const create = useAction(api.orders.create, 'Заказ записан');
  const update = useAction(api.orders.update, 'Заказ сохранён');

  const [customerId, setCustomerId] = useState<string | null>(order?.customerId ?? null);
  const [newCustomer, setNewCustomer] = useState(false);
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  const [isShop, setIsShop] = useState(false);
  const [dueDate, setDueDate] = useState<string>(order?.dueDate ?? defaultDate ?? addDays(today(), 1));
  const [dueTime, setDueTime] = useState(order?.dueTime ?? '');
  const [prepayment, setPrepayment] = useState<number | string>('');
  const [note, setNote] = useState(order?.note ?? '');
  const [items, setItems] = useState<DraftItem[]>(
    order
      ? order.items.map((item) => ({
          key: nextItemKey++,
          productId: item.productId,
          quantity: item.quantity,
          unitPrice: item.unitPrice,
          note: item.note ?? '',
        }))
      : [emptyItem()],
  );

  const finished = (products ?? []).filter((p) => p.type === 'Finished');
  // Изделие из заказа могло уйти в архив: оно всё равно должно быть видно в списке.
  const productOptions = [
    ...finished.map((p) => ({ value: p.id, label: p.name })),
    ...(order?.items ?? [])
      .filter((item) => !finished.some((p) => p.id === item.productId))
      .map((item) => ({ value: item.productId, label: item.productName })),
  ];

  const itemsLocked = order !== null && (order.status === 'Ready' || order.status === 'Delivered');
  const isNewCustomer = order === null && (newCustomer || (customers !== undefined && customers.length === 0));

  const parsedItems = items
    .filter((item) => item.productId !== null)
    .map((item) => ({
      productId: item.productId!,
      quantity: toNumber(item.quantity) ?? 0,
      unitPrice: toNumber(item.unitPrice) ?? 0,
      note: item.note.trim() || null,
    }));
  const total = parsedItems.reduce((sum, item) => sum + item.quantity * item.unitPrice, 0);
  const prepaymentNumber = toNumber(prepayment) ?? 0;

  const customerReady = isNewCustomer ? customerName.trim() !== '' : customerId !== null;
  const canSave = customerReady && parsedItems.length > 0 && parsedItems.every((item) => item.quantity > 0);

  const changeItem = (key: number, patch: Partial<DraftItem>) =>
    setItems(items.map((item) => (item.key === key ? { ...item, ...patch } : item)));

  const save = async () => {
    const common = {
      dueDate,
      dueTime: dueTime ? `${dueTime.slice(0, 5)}:00` : null,
      note: note.trim() || null,
      items: parsedItems,
    };
    if (order) {
      await update.mutateAsync({ id: order.id, customerId: customerId!, ...common });
    } else {
      let id = isNewCustomer ? null : customerId;
      if (isNewCustomer && isShop) {
        id = (await api.customers.create({ name: customerName.trim(), phone: customerPhone.trim() || null, note: null, isConsignment: true })).id;
      }
      await create.mutateAsync({
        customerId: id,
        newCustomerName: id === null ? customerName.trim() : null,
        newCustomerPhone: id === null ? customerPhone.trim() || null : null,
        prepayment: prepaymentNumber,
        ...common,
      });
    }
    onClose();
  };

  const selectedCustomer = customers?.find((c) => c.id === customerId);

  return (
    <Modal opened onClose={onClose} title={order ? `Заказ №${order.number}` : 'Новый заказ'} size="lg" centered>
      <Stack gap="sm">
        {isNewCustomer ? (
          <>
            <Group gap="sm" align="flex-end" grow>
              <TextInput
                label={isShop ? 'Название магазина' : 'Имя клиента'}
                value={customerName}
                onChange={(event) => setCustomerName(event.currentTarget.value)}
                data-autofocus
                required
              />
              <TextInput
                label="Телефон"
                placeholder="Необязательно"
                value={customerPhone}
                onChange={(event) => setCustomerPhone(event.currentTarget.value)}
              />
            </Group>
            <Checkbox
              label="Это магазин: платит после продажи, может вернуть непроданное"
              checked={isShop}
              onChange={(event) => setIsShop(event.currentTarget.checked)}
            />
          </>
        ) : (
          <Select
            label="Клиент"
            placeholder="Выберите клиента или магазин"
            data={(customers ?? []).map((c) => ({
              value: c.id,
              label: `${c.name}${c.isConsignment ? ' (магазин)' : ''}${c.phone ? ` · ${c.phone}` : ''}`,
            }))}
            value={customerId}
            onChange={setCustomerId}
            searchable
            nothingFoundMessage="Такого клиента нет"
            required
          />
        )}
        {order === null && customers !== undefined && customers.length > 0 && (
          <Group>
            <Button variant="subtle" size="compact-sm" onClick={() => setNewCustomer(!newCustomer)}>
              {newCustomer ? 'Выбрать из списка клиентов' : 'Новый клиент или магазин'}
            </Button>
          </Group>
        )}
        {selectedCustomer?.isConsignment && (
          <Text size="xs" c="grape.7">
            Магазин: затраты и выручка запишутся в день отправки, оплату внесёте, когда магазин рассчитается.
          </Text>
        )}

        <Group gap="sm" align="flex-end" grow>
          <DatePickerInput
            label={selectedCustomer?.isConsignment || isShop ? 'День отправки' : 'День выдачи'}
            value={dueDate}
            onChange={(value) => value && setDueDate(value)}
            valueFormat="D MMMM YYYY, dd"
            required
          />
          <TimePicker label="Время" value={dueTime} onChange={setDueTime} format="24h" clearable />
        </Group>

        <Text fw={600} size="sm" mt="xs">
          Что заказали
        </Text>
        {itemsLocked && (
          <Alert color="blue" variant="light">
            Заказ уже изготовлен, поэтому позиции менять нельзя. Чтобы изменить их, верните заказ в статус «Готовится».
            Если магазин что-то вернул, используйте «Отчёт магазина».
          </Alert>
        )}
        <Stack gap={6}>
          {items.map((item) => (
            <Group key={item.key} gap="xs" wrap="nowrap" align="center">
              <Select
                aria-label="Изделие"
                placeholder="Изделие"
                data={productOptions}
                value={item.productId}
                onChange={(value) => {
                  const product = finished.find((p) => p.id === value);
                  changeItem(item.key, { productId: value, unitPrice: product ? product.salePrice : item.unitPrice });
                }}
                searchable
                nothingFoundMessage="Такого изделия нет"
                disabled={itemsLocked}
                style={{ flex: 2 }}
              />
              <NumberField
                aria-label="Количество"
                placeholder="Шт"
                value={item.quantity}
                onChange={(value) => changeItem(item.key, { quantity: value })}
                disabled={itemsLocked}
                w={70}
              />
              <NumberField
                aria-label="Цена за штуку"
                placeholder="Цена"
                value={item.unitPrice}
                onChange={(value) => changeItem(item.key, { unitPrice: value })}
                disabled={itemsLocked}
                w={90}
              />
              <TextInput
                aria-label="Пожелания"
                placeholder="Пожелания"
                value={item.note}
                onChange={(event) => changeItem(item.key, { note: event.currentTarget.value })}
                disabled={itemsLocked}
                style={{ flex: 2 }}
              />
              <ActionIcon
                variant="subtle"
                color="gray"
                disabled={itemsLocked}
                onClick={() => setItems(items.length === 1 ? [emptyItem()] : items.filter((i) => i.key !== item.key))}
                aria-label="Убрать позицию"
              >
                <IconTrash size={16} />
              </ActionIcon>
            </Group>
          ))}
        </Stack>
        {!itemsLocked && (
          <Group>
            <Button variant="light" size="compact-sm" leftSection={<IconPlus size={14} />} onClick={() => setItems([...items, emptyItem()])}>
              Ещё изделие
            </Button>
          </Group>
        )}

        <Group gap="sm" align="flex-end" grow>
          {order === null ? (
            <NumberField label={`Предоплата, ${currency}`} value={prepayment} onChange={setPrepayment} />
          ) : (
            <Text size="sm" c="dimmed">
              Оплаты вносятся кнопкой «Оплата» у заказа. Оплачено: {money(order.paid)}
            </Text>
          )}
          <div>
            <Text size="sm">
              Сумма заказа: <b>{money(total)}</b> {currency}
            </Text>
            <Text size="sm" c="dimmed">
              Осталось оплатить: {money(total - (order ? order.paid : prepaymentNumber))}
            </Text>
          </div>
        </Group>

        <Textarea
          label="Заметка"
          placeholder="Надпись на торте, адрес доставки и так далее"
          value={note}
          onChange={(event) => setNote(event.currentTarget.value)}
          autosize
          minRows={2}
        />

        <Group justify="flex-end" gap="sm" mt="xs">
          <Button variant="default" onClick={onClose}>
            Отмена
          </Button>
          <Button onClick={() => void save().catch(() => undefined)} disabled={!canSave} loading={create.isPending || update.isPending}>
            Сохранить
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
