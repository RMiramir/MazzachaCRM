import {
  ActionIcon,
  Alert,
  Anchor,
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
  UnstyledButton,
} from '@mantine/core';
import { DatePickerInput, TimePicker } from '@mantine/dates';
import { notifications } from '@mantine/notifications';
import {
  IconArrowsExchange,
  IconBuildingStore,
  IconCash,
  IconChevronDown,
  IconCopy,
  IconDotsVertical,
  IconPencil,
  IconPlus,
  IconReceipt,
  IconTrash,
  IconTruckDelivery,
  IconX,
} from '@tabler/icons-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { useAction, useCurrency, useCustomers, useFreeStock, useProducts } from '../api/hooks';
import type { Order, OrderStatus } from '../api/types';
import { addDays, longDate, money, orderStatusColor, orderStatusLabel, quantity, shortDate, shortTime, toNumber, today } from '../lib/format';
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
        done: 'Заказ готов: затраты и выручка записаны в день приготовления, сырьё списано',
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

const STEPS: OrderStatus[] = ['New', 'InProgress', 'Ready', 'Delivered'];

const earliest = (a: string, b: string) => (a < b ? a : b);

/**
 * В какой день записать новый статус. «Готов» — в день приготовления, но не позже сегодняшнего:
 * сделали раньше плана — заказ переезжает на сегодня. «Выдан» — в день выдачи, но не позже сегодняшнего.
 */
export function statusDate(order: Order, status: OrderStatus): string {
  if (status === 'Ready') return earliest(order.productionDate, today());
  if (status === 'Delivered') return earliest(order.dueDate, today());
  return today();
}

/** Подпись дня: «сегодня», «завтра» или дата. */
function dayLabel(date: string): string {
  if (date === today()) return 'сегодня';
  if (date === addDays(today(), 1)) return 'завтра';
  if (date === addDays(today(), -1)) return 'вчера';
  return longDate(date);
}

const stepLabel = (order: Order, status: OrderStatus) =>
  status === 'Delivered' && order.isConsignment ? 'Отправлен' : orderStatusLabel[status];

function statusDone(order: Order, status: OrderStatus, date: string): string {
  switch (status) {
    case 'New':
      return 'Заказ возвращён в начало';
    case 'InProgress':
      return order.status === 'Ready' ? 'Заказ возвращён в работу' : 'Заказ взят в работу';
    case 'Ready':
      if (order.status === 'Delivered') return 'Отметка о выдаче снята';
      return date === order.productionDate
        ? 'Заказ готов: затраты и выручка записаны в день приготовления, сырьё списано'
        : `Заказ готов раньше плана: он перенесён на ${dayLabel(date)}, затраты и выручка записаны туда`;
    case 'Delivered':
      return order.isConsignment ? 'Поставка отправлена' : 'Заказ выдан';
    default:
      return 'Заказ отменён';
  }
}

/**
 * Перевод заказа в другой статус. Там, где действие убирает изделия из дня
 * или отменяет заказ, сначала спрашивает подтверждение. Дату статуса выбирает {@link statusDate}.
 */
export function useChangeStatus(order: Order) {
  const confirm = useConfirm();
  const setStatus = useAction(api.orders.setStatus);
  const produced = order.status === 'Ready' || order.status === 'Delivered';

  const change = async (status: OrderStatus) => {
    if (status === order.status) return;

    if (status === 'Cancelled') {
      const ok = await confirm({
        title: `Отменить заказ №${order.number}?`,
        message: produced
          ? 'Заказ уже изготовлен. Изделия останутся в таблице дня как обычный остаток: их можно продать или списать.'
          : 'Заказ останется в списке со статусом «Отменён». Изделия, взятые из готового, вернутся на витрину.',
        confirmLabel: 'Отменить заказ',
        danger: true,
      });
      if (!ok) return;
    } else if (produced && (status === 'New' || status === 'InProgress')) {
      const ok = await confirm({
        title: `Вернуть заказ №${order.number} в работу?`,
        message:
          'Изделия, испечённые под этот заказ, уберутся из таблицы дня, сырьё вернётся на склад. Взятое из готового останется за заказом.',
        confirmLabel: 'Вернуть в работу',
      });
      if (!ok) return;
    }

    const date = statusDate(order, status);
    await setStatus.mutateAsync({ id: order.id, status, date });
    notifications.show({ message: `№${order.number}. ${statusDone(order, status, date)}`, color: 'teal', autoClose: 4000 });
  };

  return { change, pending: setStatus.isPending, target: setStatus.variables?.status };
}

/** Полоса шагов заказа: нажатие на шаг сразу переводит заказ в этот статус. */
export function OrderStepper({ order }: { order: Order }) {
  const { change, pending, target } = useChangeStatus(order);

  if (order.status === 'Cancelled') {
    return (
      <Group gap={8}>
        <Badge color="red" variant="light">
          Отменён
        </Badge>
        <Button variant="subtle" size="compact-sm" onClick={() => void change('New').catch(() => undefined)} loading={pending}>
          Вернуть в работу
        </Button>
      </Group>
    );
  }

  const current = STEPS.indexOf(order.status);
  return (
    <Button.Group aria-label={`Статус заказа №${order.number}`}>
      {STEPS.map((step, index) => {
        const isCurrent = index === current;
        const allowed = allowedTargets[order.status].includes(step);
        return (
          <Button
            key={step}
            size="compact-sm"
            variant={isCurrent ? 'filled' : index < current ? 'light' : 'default'}
            aria-pressed={isCurrent}
            disabled={!isCurrent && !allowed}
            loading={pending && target === step}
            onClick={() => {
              if (!isCurrent) void change(step).catch(() => undefined);
            }}
          >
            {stepLabel(order, step)}
          </Button>
        );
      })}
    </Button.Group>
  );
}

/** Статус в таблице заказов: нажатие открывает список, куда можно перевести заказ. */
export function OrderStatusMenu({ order }: { order: Order }) {
  const { change } = useChangeStatus(order);
  return (
    <Menu position="bottom-start" withinPortal>
      <Menu.Target>
        <UnstyledButton aria-label={`Сменить статус заказа №${order.number}`}>
          <Group gap={2} wrap="nowrap">
            <OrderStatusBadge order={order} />
            <IconChevronDown size={14} color="var(--mantine-color-gray-6)" />
          </Group>
        </UnstyledButton>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>Перевести в статус</Menu.Label>
        {allowedTargets[order.status].map((status) => (
          <Menu.Item
            key={status}
            color={status === 'Cancelled' ? 'red' : undefined}
            onClick={() => void change(status).catch(() => undefined)}
          >
            {status === 'Cancelled' ? 'Отменить заказ' : stepLabel(order, status)}
          </Menu.Item>
        ))}
      </Menu.Dropdown>
    </Menu>
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
    <Badge size="md" variant="light" color="grape" leftSection={<IconBuildingStore size={12} />}>
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
 * `actionDate` — день, которым по умолчанию записываются оплата и отчёт магазина.
 */
export function OrderActions({
  order,
  actionDate,
  compact,
  paymentOnly,
}: {
  order: Order;
  actionDate: string;
  compact?: boolean;
  /** Рядом уже есть полоса шагов: показывать только кнопку оплаты и меню. */
  paymentOnly?: boolean;
}) {
  const confirm = useConfirm();
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const { change, pending } = useChangeStatus(order);
  const remove = useAction(api.orders.remove, 'Заказ удалён');

  const next = nextStep(order);
  const step = paymentOnly && next?.kind !== 'payment' ? null : next;
  const produced = order.status === 'Ready' || order.status === 'Delivered';
  const active = order.status !== 'Delivered' && order.status !== 'Cancelled';

  const runStep = async () => {
    if (!step) return;
    if (step.kind === 'payment') {
      setDialog({ kind: 'payment' });
      return;
    }
    await change(step.status);
  };

  const cancel = () => change('Cancelled');

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
            loading={pending}
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
              Статус с выбором даты
            </Menu.Item>
            {active && (
              <Menu.Item leftSection={<IconX size={16} />} onClick={() => void cancel().catch(() => undefined)}>
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
      {dialog?.kind === 'status' && <StatusModal order={order} onClose={() => setDialog(null)} />}
    </>
  );
}

/** Когда выдать: время, а если выдача в другой день — дата крупно. */
function DueText({ order }: { order: Order }) {
  const time = order.dueTime ? shortTime(order.dueTime) : null;
  if (order.dueDate === order.productionDate) {
    return time ? <>К {time}</> : null;
  }
  const verb = order.isConsignment ? 'Отправка' : 'Выдача';
  return (
    <Text span inherit fw={600} c="blue.8">
      <IconTruckDelivery size={14} style={{ verticalAlign: -2, marginRight: 4 }} />
      {verb} {dayLabel(order.dueDate)}
      {time && `, ${time}`}
    </Text>
  );
}

/** Карточка заказа для экрана «День» (в день приготовления) и «Обзора». */
export function OrderCard({
  order,
  actionDate,
  showProductionDate,
}: {
  order: Order;
  actionDate: string;
  /** Показать день приготовления: для списков, где заказы разных дней. */
  showProductionDate?: boolean;
}) {
  const done = order.status === 'Delivered' && order.isPaid;
  const contact = [order.customerPhone].filter(Boolean).join(' · ');
  return (
    <Paper withBorder radius="md" p="sm" style={{ opacity: done ? 0.75 : 1 }}>
      <Group justify="space-between" align="flex-start" wrap="nowrap" gap="sm">
        <Stack gap={2} style={{ minWidth: 0 }}>
          <Group gap={8} wrap="wrap">
            <Text fw={600}>
              №{order.number} · {order.customerName}
            </Text>
            {order.isConsignment && <ShopBadge />}
            {order.status === 'Delivered' && <OrderStatusBadge order={order} />}
          </Group>
          <Group mt={4} mb={2}>
            <OrderStepper order={order} />
          </Group>
          <Text size="sm" c="dimmed">
            {showProductionDate && (
              <>
                Приготовить {dayLabel(order.productionDate)}
                {' · '}
              </>
            )}
            <DueText order={order} />
            {contact && (order.dueTime || order.dueDate !== order.productionDate ? ' · ' : '')}
            {contact}
          </Text>
          <Text size="sm">{order.itemsSummary}</Text>
          {order.items.some((i) => i.reservedBatchId !== null) && (
            <Text size="xs" c="dimmed">
              Из готового:{' '}
              {order.items
                .filter((i) => i.reservedBatchId !== null)
                .map((i) => `${i.productName} × ${quantity(i.quantity)}`)
                .join(', ')}
            </Text>
          )}
          {order.items.some((i) => i.returned > 0) && (
            <Text size="xs" c="grape.7">
              Возврат: {order.items.filter((i) => i.returned > 0).map((i) => `${i.productName} × ${quantity(i.returned)}${i.returnedWrittenOff > 0 ? (i.returnedWrittenOff === i.returned ? ', списан' : `, списано ${quantity(i.returnedWrittenOff)}`) : ''}`).join('; ')}
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
        <OrderActions order={order} actionDate={actionDate} paymentOnly />
      </Group>
    </Paper>
  );
}

/**
 * Короткая строка для дня выдачи: заказ приготовлен в другой день, здесь только напоминание,
 * что его нужно выдать или доставить, и кнопка «Выдать».
 */
export function DeliveryRow({ order, actionDate }: { order: Order; actionDate: string }) {
  const { change, pending } = useChangeStatus(order);
  const [paying, setPaying] = useState(false);
  const produced = order.status === 'Ready' || order.status === 'Delivered';
  const delivered = order.status === 'Delivered';
  const verb = order.isConsignment ? 'Отправить' : 'Выдать';

  return (
    <Group className="delivery-row" justify="space-between" wrap="nowrap" gap="sm">
      <Group gap={6} wrap="wrap" style={{ minWidth: 0 }}>
        <IconTruckDelivery size={16} color="var(--mantine-color-gray-6)" style={{ flexShrink: 0 }} />
        {order.dueTime && (
          <Text size="sm" fw={600} c="gray.7">
            {shortTime(order.dueTime)}
          </Text>
        )}
        <Text size="sm" fw={500} c={delivered ? 'dimmed' : undefined} td={delivered ? 'line-through' : undefined}>
          №{order.number} {order.customerName}
        </Text>
        <Text size="sm" c="dimmed">
          {[order.customerPhone, order.itemsSummary, order.note].filter(Boolean).join(' · ')}
        </Text>
        <Anchor component={Link} to={`/day/${order.productionDate}`} size="xs" c="dimmed">
          {produced ? 'приготовлен' : 'готовится'} {dayLabel(order.productionDate)}
        </Anchor>
        {produced && order.balance > 0 && (
          <Text size="xs" c="orange.8">
            к оплате {money(order.balance)}
          </Text>
        )}
      </Group>
      <Group gap={4} wrap="nowrap" style={{ flexShrink: 0 }}>
        {order.status === 'Ready' && (
          <Button size="compact-sm" variant="light" onClick={() => void change('Delivered').catch(() => undefined)} loading={pending}>
            {verb}
          </Button>
        )}
        {(order.status === 'New' || order.status === 'InProgress') && (
          <Text size="xs" c="dimmed">
            ещё не готов
          </Text>
        )}
        {delivered && order.balance > 0 && (
          <Button size="compact-sm" variant="subtle" color="orange" onClick={() => setPaying(true)}>
            Оплата
          </Button>
        )}
        {delivered && order.balance <= 0 && (
          <Text size="xs" c="teal.8">
            {order.isConsignment ? 'отправлен' : 'выдан'}
          </Text>
        )}
      </Group>
      {paying && <PaymentModal order={order} date={actionDate} onClose={() => setPaying(false)} />}
    </Group>
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
  const [writtenOff, setWrittenOff] = useState<Record<string, number | string>>(
    Object.fromEntries(order.items.map((item) => [item.id, item.returnedWrittenOff])),
  );

  const rows = order.items.map((item) => {
    const soldNumber = toNumber(sold[item.id]) ?? 0;
    const returned = item.quantity - soldNumber;
    const writeOff = toNumber(writtenOff[item.id]) ?? 0;
    return {
      item,
      sold: soldNumber,
      returned,
      writeOff,
      invalid: soldNumber > item.quantity || writeOff > Math.max(returned, 0),
    };
  });
  const allWrittenOff = rows.every((row) => row.returned <= 0 || row.writeOff === row.returned);
  const writeOffAll = (checked: boolean) =>
    setWrittenOff(Object.fromEntries(rows.map((row) => [row.item.id, checked ? Math.max(row.returned, 0) : 0])));
  const total = rows.reduce((sum, row) => sum + Math.max(row.sold, 0) * row.item.unitPrice, 0);
  const invalid = rows.some((row) => row.invalid);

  const save = async () => {
    await report.mutateAsync({
      id: order.id,
      date: reportDate,
      items: rows.map((row) => ({ itemId: row.item.id, sold: row.sold, writtenOff: Math.min(row.writeOff, Math.max(row.returned, 0)) })),
    });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={`${order.isConsignment ? 'Отчёт магазина' : 'Возврат'}: №${order.number}, ${order.customerName}`} size="lg" centered>
      <Stack gap="sm">
        <Text size="sm" c="dimmed">
          Укажите, сколько продано. Остальное — возврат: выручка дня отправки ({shortDate(order.producedDate)}) уменьшится.
          Возврат можно сразу списать. Несписанный останется остатком дня отправки: перенесите его на нужный день
          кнопкой «Остатки», и там его можно продать.
        </Text>
        <Table verticalSpacing={6}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Изделие</Table.Th>
              <Table.Th className="num">Отправлено</Table.Th>
              <Table.Th className="num">Продано</Table.Th>
              <Table.Th className="num">Вернули</Table.Th>
              <Table.Th className="num">Списать сразу</Table.Th>
              <Table.Th className="num">К оплате</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map(({ item, sold: soldNumber, returned, writeOff, invalid: rowInvalid }) => (
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
                <Table.Td className="num">
                  <Group justify="flex-end">
                    <NumberField
                      aria-label={`Списать сразу: ${item.productName}`}
                      size="xs"
                      w={80}
                      value={writtenOff[item.id]}
                      onChange={(value) => setWrittenOff({ ...writtenOff, [item.id]: value })}
                      error={writeOff > Math.max(returned, 0)}
                      disabled={returned <= 0}
                      decimalScale={3}
                    />
                  </Group>
                </Table.Td>
                <Table.Td className="num">{money(Math.max(soldNumber, 0) * item.unitPrice)}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
        <Checkbox
          label="Списать весь возврат сразу"
          checked={allWrittenOff && rows.some((row) => row.returned > 0)}
          disabled={!rows.some((row) => row.returned > 0)}
          onChange={(event) => writeOffAll(event.currentTarget.checked)}
        />
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

function StatusModal({ order, onClose }: { order: Order; onClose: () => void }) {
  const setStatus = useAction(api.orders.setStatus, 'Статус изменён');
  const targets = allowedTargets[order.status];
  const [status, setStatusValue] = useState<OrderStatus>(targets[0]);
  const [date, setDate] = useState<string>(statusDate(order, targets[0]));

  const produced = order.status === 'Ready' || order.status === 'Delivered';
  const dateLabel =
    status === 'Ready' && !produced ? 'День приготовления' : status === 'Delivered' ? 'День выдачи' : null;

  const explanation: Record<OrderStatus, string> = {
    New: produced ? 'Изделия заказа будут убраны из таблицы дня, сырьё вернётся на склад.' : 'Заказ вернётся в начало.',
    InProgress: produced
      ? 'Изделия заказа будут убраны из таблицы дня, сырьё вернётся на склад.'
      : 'Заказ отмечается как взятый в работу. На склад и на цифры дня это не влияет.',
    Ready:
      order.status === 'Delivered'
        ? 'Снимается только отметка о выдаче, цифры дня не меняются.'
        : 'Заказ переедет на выбранный день: там его затраты и выручка, сырьё списывается.',
    Delivered: produced
      ? 'Отметка о выдаче. Цифры дня не меняются.'
      : 'Заказ будет отмечен приготовленным в свой день приготовления (если он позже — в день выдачи) и выданным.',
    Cancelled: produced
      ? 'Изготовленные изделия останутся в таблице дня как обычный остаток: их можно продать или списать.'
      : 'Заказ останется в списке со статусом «Отменён».',
  };

  const save = async () => {
    await setStatus.mutateAsync({ id: order.id, status, date });
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
          onChange={(value) => {
            if (!value) return;
            setStatusValue(value as OrderStatus);
            setDate(statusDate(order, value as OrderStatus));
          }}
          allowDeselect={false}
        />
        {dateLabel && (
          <DatePickerInput label={dateLabel} value={date} onChange={(value) => value && setDate(value)} valueFormat="D MMMM YYYY" />
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
  /** Строка дня, из которой берутся готовые изделия. Пусто — приготовить под заказ. */
  fromBatchId: string | null;
}

let nextItemKey = 1;
const emptyItem = (): DraftItem => ({
  key: nextItemKey++,
  productId: null,
  quantity: 1,
  unitPrice: '',
  note: '',
  fromBatchId: null,
});

/**
 * Новый заказ или изменение существующего. `defaultDate` — день, открытый в календаре:
 * он подставляется в день приготовления и в день выдачи нового заказа.
 */
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
  const [productionDate, setProductionDate] = useState<string>(order?.productionDate ?? defaultDate ?? today());
  const [dueDate, setDueDate] = useState<string>(order?.dueDate ?? defaultDate ?? today());
  const [dueTime, setDueTime] = useState(order?.dueTime ?? '');
  const { data: freeStock } = useFreeStock(order?.id ?? null, productionDate);
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
          fromBatchId: item.reservedBatchId,
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
      fromBatchId: item.fromBatchId,
    }));

  // Готовые изделия: что доступно по каждому изделию и не взято ли больше, чем есть.
  const stockFor = (productId: string | null) => (freeStock ?? []).filter((s) => s.productId === productId);
  const takenFrom = (batchId: string) =>
    parsedItems.filter((i) => i.fromBatchId === batchId).reduce((sum, i) => sum + i.quantity, 0);
  const shortage = (item: DraftItem) => {
    if (!item.fromBatchId) return null;
    const source = (freeStock ?? []).find((s) => s.batchId === item.fromBatchId);
    return source && takenFrom(source.batchId) > source.available ? source.available : null;
  };
  const stockOk = items.every((item) => shortage(item) === null);

  const total = parsedItems.reduce((sum, item) => sum + item.quantity * item.unitPrice, 0);
  const prepaymentNumber = toNumber(prepayment) ?? 0;

  const customerReady = isNewCustomer ? customerName.trim() !== '' : customerId !== null;
  const canSave = customerReady && parsedItems.length > 0 && parsedItems.every((item) => item.quantity > 0) && stockOk;

  const changeItem = (key: number, patch: Partial<DraftItem>) =>
    setItems(items.map((item) => (item.key === key ? { ...item, ...patch } : item)));

  // День выдачи идёт следом за днём приготовления, если совпадал с ним или стал раньше.
  const changeProductionDate = (value: string) => {
    if (dueDate === productionDate || dueDate < value) setDueDate(value);
    setProductionDate(value);
    // Готовые изделия другого дня могут быть недоступны: выбираем заново.
    setItems((current) => current.map((item) => ({ ...item, fromBatchId: null })));
  };

  const save = async () => {
    const common = {
      productionDate,
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
            label="Приготовить"
            description="В этот день — затраты и прибыль"
            value={productionDate}
            onChange={(value) => value && changeProductionDate(value)}
            valueFormat="D MMMM YYYY, dd"
            disabled={itemsLocked}
            required
          />
          <DatePickerInput
            label={selectedCustomer?.isConsignment || isShop ? 'Отправить в магазин' : 'Выдать или доставить'}
            description="Можно позже дня приготовления"
            value={dueDate}
            minDate={productionDate}
            onChange={(value) => value && setDueDate(value)}
            valueFormat="D MMMM YYYY, dd"
            required
          />
          <TimePicker label="Время" description="Необязательно" value={dueTime} onChange={setDueTime} format="24h" clearable />
        </Group>
        {itemsLocked ? (
          <Text size="xs" c="dimmed">
            Заказ уже приготовлен {longDate(productionDate)}. Чтобы перенести день приготовления, верните его в работу.
          </Text>
        ) : (
          dueDate !== productionDate && (
            <Text size="xs" c="blue.8">
              {longDate(productionDate)} заказ будет в таблице дня с затратами и прибылью, а {longDate(dueDate)} — короткой
              строкой в «Выдаче и доставке».
            </Text>
          )
        )}

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
            <div key={item.key}>
            <Group gap="xs" wrap="nowrap" align="center">
              <Select
                aria-label="Изделие"
                placeholder="Изделие"
                data={productOptions}
                value={item.productId}
                onChange={(value) => {
                  const product = finished.find((p) => p.id === value);
                  changeItem(item.key, {
                    productId: value,
                    unitPrice: product ? product.salePrice : item.unitPrice,
                    fromBatchId: null,
                  });
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
            {stockFor(item.productId).length > 0 && (
              <Group gap="xs" mt={4} align="center" wrap="wrap">
                <Text size="xs" c="dimmed">
                  Откуда:
                </Text>
                <Select
                  size="xs"
                  aria-label="Откуда взять изделие"
                  w={340}
                  allowDeselect={false}
                  disabled={itemsLocked}
                  data={[
                    { value: 'bake', label: 'Приготовить под заказ' },
                    ...stockFor(item.productId).map((source) => ({
                      value: source.batchId,
                      label: `Из готового, ${dayLabel(source.productionDate)}: ${
                        source.reservedForThisOrder ? 'уже в этом заказе' : 'свободно'
                      } ${quantity(source.available)} шт`,
                    })),
                  ]}
                  value={item.fromBatchId ?? 'bake'}
                  onChange={(value) => changeItem(item.key, { fromBatchId: value === 'bake' ? null : value })}
                />
                {shortage(item) !== null && (
                  <Text size="xs" c="red.7">
                    Есть только {quantity(shortage(item))} шт. Остальное добавьте отдельной позицией «Приготовить».
                  </Text>
                )}
              </Group>
            )}
            </div>
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
