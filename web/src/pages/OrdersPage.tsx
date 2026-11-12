import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Group,
  Menu,
  Modal,
  SegmentedControl,
  Select,
  Stack,
  Table,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core';
import { DatePickerInput, TimePicker } from '@mantine/dates';
import { notifications } from '@mantine/notifications';
import { IconArrowsExchange, IconDotsVertical, IconPencil, IconPlus, IconTrash, IconX } from '@tabler/icons-react';
import { useState } from 'react';
import { api } from '../api/client';
import { useAction, useCurrency, useCustomers, useOrders, useProducts } from '../api/hooks';
import type { Order, OrderStatus } from '../api/types';
import { NumberField } from '../components/inputs';
import { EmptyState, Loading, PageHeader, Profit, Section, useConfirm } from '../components/ui';
import {
  addDays,
  money,
  orderStatusColor,
  orderStatusLabel,
  quantity,
  shortDate,
  shortTime,
  toNumber,
  today,
} from '../lib/format';

/** Следующий шаг заказа: одна кнопка в строке таблицы. */
const nextStep: Partial<Record<OrderStatus, { status: OrderStatus; label: string; done: string }>> = {
  New: { status: 'InProgress', label: 'В работу', done: 'Заказ взят в работу' },
  InProgress: {
    status: 'Ready',
    label: 'Готов',
    done: 'Заказ готов: сырьё списано, себестоимость записана в затраты сегодняшнего дня',
  },
  Ready: { status: 'Completed', label: 'Выдать', done: 'Заказ выдан: выручка записана на сегодня' },
};

const allowedTargets: Record<OrderStatus, OrderStatus[]> = {
  New: ['InProgress', 'Ready', 'Completed', 'Cancelled'],
  InProgress: ['New', 'Ready', 'Completed', 'Cancelled'],
  Ready: ['New', 'InProgress', 'Completed', 'Cancelled'],
  Completed: ['Ready'],
  Cancelled: ['New'],
};

export function OrdersPage() {
  const [scope, setScope] = useState('active');
  const { data: orders, error } = useOrders({ activeOnly: scope === 'active' });
  const confirm = useConfirm();

  const [editing, setEditing] = useState<Order | 'new' | null>(null);
  const [statusFor, setStatusFor] = useState<Order | null>(null);

  const setStatus = useAction(api.orders.setStatus);
  const remove = useAction(api.orders.remove, 'Заказ удалён');

  const advance = async (order: Order) => {
    const step = nextStep[order.status];
    if (!step) return;
    await setStatus.mutateAsync({ id: order.id, status: step.status, date: today() });
    notifications.show({ message: `№${order.number}. ${step.done}`, color: 'teal' });
  };

  const cancel = async (order: Order) => {
    const produced = order.status === 'Ready';
    const ok = await confirm({
      title: `Отменить заказ №${order.number}?`,
      message: produced
        ? 'Заказ уже изготовлен. Изделия останутся в таблице дня как обычный остаток: их можно продать или списать.'
        : 'Заказ останется в списке со статусом «Отменён».',
      confirmLabel: 'Отменить заказ',
      danger: true,
    });
    if (ok) setStatus.mutate({ id: order.id, status: 'Cancelled', date: today() });
  };

  const removeOrder = async (order: Order) => {
    const ok = await confirm({
      title: `Удалить заказ №${order.number}?`,
      message: `${order.customerName}: ${order.itemsSummary}. Удаление нельзя отменить.`,
      confirmLabel: 'Удалить',
      danger: true,
    });
    if (ok) remove.mutate(order.id);
  };

  return (
    <Stack gap="md">
      <PageHeader title="Заказы">
        <SegmentedControl
          value={scope}
          onChange={setScope}
          data={[
            { value: 'active', label: 'Активные' },
            { value: 'all', label: 'Все' },
          ]}
        />
        <Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>
          Новый заказ
        </Button>
      </PageHeader>

      {!orders ? (
        <Loading error={error} />
      ) : orders.length === 0 ? (
        <Section>
          <EmptyState title={scope === 'active' ? 'Активных заказов нет' : 'Заказов пока нет'}>
            Запишите предзаказ: кто заказал, что и к какому дню. Когда заказ будет готов и выдан, его затраты и
            выручка попадут в таблицу дня.
          </EmptyState>
        </Section>
      ) : (
        <Section>
          <div className="table-scroll">
            <Table verticalSpacing="xs" horizontalSpacing="sm" highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>№</Table.Th>
                  <Table.Th>Выдача</Table.Th>
                  <Table.Th>Клиент</Table.Th>
                  <Table.Th>Что заказали</Table.Th>
                  <Table.Th className="num">Сумма</Table.Th>
                  <Table.Th className="num">Осталось оплатить</Table.Th>
                  <Table.Th>Статус</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {orders.map((order) => {
                  const step = nextStep[order.status];
                  const active = order.status !== 'Completed' && order.status !== 'Cancelled';
                  const overdue = active && order.dueDate < today();
                  return (
                    <Table.Tr key={order.id}>
                      <Table.Td className="tabular">{order.number}</Table.Td>
                      <Table.Td>
                        <Text size="sm" fw={order.dueDate === today() && active ? 700 : undefined} c={overdue ? 'red.7' : undefined}>
                          {order.dueDate === today() ? 'Сегодня' : shortDate(order.dueDate)}
                          {order.dueTime && `, ${shortTime(order.dueTime)}`}
                        </Text>
                        {overdue && (
                          <Text size="xs" c="red.7">
                            срок прошёл
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm" fw={500}>
                          {order.customerName}
                        </Text>
                        {order.customerPhone && (
                          <Text size="xs" c="dimmed">
                            {order.customerPhone}
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm">{order.itemsSummary}</Text>
                        {order.note && (
                          <Text size="xs" c="dimmed">
                            {order.note}
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td className="num">
                        {money(order.total)}
                        {order.profit !== null && (
                          <Text size="xs" c="dimmed">
                            прибыль <Profit value={order.profit} />
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td className="num">
                        {order.balance > 0 ? (
                          <Text span inherit fw={600}>
                            {money(order.balance)}
                          </Text>
                        ) : order.balance < 0 ? (
                          <Text span inherit c="dimmed">
                            переплата {money(-order.balance)}
                          </Text>
                        ) : (
                          <Text span inherit c="dimmed">
                            оплачен
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td>
                        <Badge color={orderStatusColor[order.status]} variant="light">
                          {orderStatusLabel[order.status]}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        <Group gap={4} justify="flex-end" wrap="nowrap">
                          {step && (
                            <Button
                              variant="light"
                              size="compact-sm"
                              onClick={() => void advance(order).catch(() => undefined)}
                              loading={setStatus.isPending && setStatus.variables?.id === order.id}
                            >
                              {step.label}
                            </Button>
                          )}
                          <Menu position="bottom-end" withinPortal>
                            <Menu.Target>
                              <ActionIcon variant="subtle" color="gray" aria-label={`Действия: заказ №${order.number}`}>
                                <IconDotsVertical size={16} />
                              </ActionIcon>
                            </Menu.Target>
                            <Menu.Dropdown>
                              <Menu.Item leftSection={<IconPencil size={16} />} onClick={() => setEditing(order)}>
                                Изменить
                              </Menu.Item>
                              <Menu.Item leftSection={<IconArrowsExchange size={16} />} onClick={() => setStatusFor(order)}>
                                Сменить статус
                              </Menu.Item>
                              {active && (
                                <Menu.Item leftSection={<IconX size={16} />} onClick={() => cancel(order)}>
                                  Отменить заказ
                                </Menu.Item>
                              )}
                              <Menu.Divider />
                              <Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => removeOrder(order)}>
                                Удалить
                              </Menu.Item>
                            </Menu.Dropdown>
                          </Menu>
                        </Group>
                      </Table.Td>
                    </Table.Tr>
                  );
                })}
              </Table.Tbody>
            </Table>
          </div>
        </Section>
      )}

      {editing !== null && <OrderEditor order={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {statusFor && <StatusModal order={statusFor} onClose={() => setStatusFor(null)} />}
    </Stack>
  );
}

// ---------- Смена статуса с выбором даты ----------

function StatusModal({ order, onClose }: { order: Order; onClose: () => void }) {
  const setStatus = useAction(api.orders.setStatus, 'Статус изменён');
  const targets = allowedTargets[order.status];
  const [status, setStatusValue] = useState<OrderStatus>(targets[0]);
  const [date, setDate] = useState<string>(today());

  const produced = order.status === 'Ready' || order.status === 'Completed';
  const needsDate = status === 'Ready' || status === 'Completed';

  const explanation: Record<OrderStatus, string> = {
    New: produced
      ? 'Изделия заказа будут убраны из таблицы дня, сырьё вернётся на склад.'
      : 'Заказ вернётся в начало.',
    InProgress: produced
      ? 'Изделия заказа будут убраны из таблицы дня, сырьё вернётся на склад.'
      : 'Заказ отмечается как взятый в работу. На склад и на цифры дня это не влияет.',
    Ready:
      order.status === 'Completed'
        ? 'Выдача будет отменена: выручка по заказу уберётся из дня выдачи.'
        : 'Изделия попадут в таблицу выбранного дня как изготовленные под заказ, сырьё спишется со склада.',
    Completed: `Выручка ${money(order.total)} попадёт в выбранный день.`,
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
        </Text>
        <Select
          label="Новый статус"
          data={targets.map((target) => ({ value: target, label: orderStatusLabel[target] }))}
          value={status}
          onChange={(value) => value && setStatusValue(value as OrderStatus)}
          allowDeselect={false}
        />
        {needsDate && (
          <DatePickerInput
            label={status === 'Ready' ? 'День изготовления' : 'День выдачи'}
            value={date}
            onChange={(value) => value && setDate(value)}
            valueFormat="D MMMM YYYY"
          />
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

// ---------- Карточка заказа ----------

interface DraftItem {
  key: number;
  productId: string | null;
  quantity: number | string;
  unitPrice: number | string;
  note: string;
}

let nextItemKey = 1;
const emptyItem = (): DraftItem => ({ key: nextItemKey++, productId: null, quantity: 1, unitPrice: '', note: '' });

function OrderEditor({ order, onClose }: { order: Order | null; onClose: () => void }) {
  const currency = useCurrency();
  const { data: customers } = useCustomers();
  const { data: products } = useProducts(false);
  const create = useAction(api.orders.create, 'Заказ записан');
  const update = useAction(api.orders.update, 'Заказ сохранён');

  const [customerId, setCustomerId] = useState<string | null>(order?.customerId ?? null);
  const [newCustomer, setNewCustomer] = useState(false);
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  const [dueDate, setDueDate] = useState<string>(order?.dueDate ?? addDays(today(), 1));
  const [dueTime, setDueTime] = useState(order?.dueTime ?? '');
  const [prepayment, setPrepayment] = useState<number | string>(order?.prepayment || '');
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

  const itemsLocked = order !== null && (order.status === 'Ready' || order.status === 'Completed');
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
      prepayment: prepaymentNumber,
      note: note.trim() || null,
      items: parsedItems,
    };
    if (order) {
      await update.mutateAsync({ id: order.id, customerId: customerId!, ...common });
    } else {
      await create.mutateAsync({
        customerId: isNewCustomer ? null : customerId,
        newCustomerName: isNewCustomer ? customerName.trim() : null,
        newCustomerPhone: isNewCustomer ? customerPhone.trim() || null : null,
        ...common,
      });
    }
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={order ? `Заказ №${order.number}` : 'Новый заказ'} size="lg" centered>
      <Stack gap="sm">
        {isNewCustomer ? (
          <Group gap="sm" align="flex-end" grow>
            <TextInput
              label="Имя клиента"
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
        ) : (
          <Select
            label="Клиент"
            placeholder="Выберите клиента"
            data={(customers ?? []).map((c) => ({ value: c.id, label: c.phone ? `${c.name} · ${c.phone}` : c.name }))}
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
              {newCustomer ? 'Выбрать из списка клиентов' : 'Новый клиент'}
            </Button>
          </Group>
        )}

        <Group gap="sm" align="flex-end" grow>
          <DatePickerInput
            label="День выдачи"
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
            <Button
              variant="light"
              size="compact-sm"
              leftSection={<IconPlus size={14} />}
              onClick={() => setItems([...items, emptyItem()])}
            >
              Ещё изделие
            </Button>
          </Group>
        )}

        <Group gap="sm" align="flex-end" grow>
          <NumberField label={`Предоплата, ${currency}`} value={prepayment} onChange={setPrepayment} />
          <div>
            <Text size="sm">
              Сумма заказа: <b>{money(total)}</b> {currency}
            </Text>
            <Text size="sm" c="dimmed">
              Осталось оплатить: {money(total - prepaymentNumber)}
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

        <Group justify="space-between" mt="xs">
          <Text size="xs" c="dimmed">
            {quantity(parsedItems.length)} поз.
          </Text>
          <Group gap="sm">
            <Button variant="default" onClick={onClose}>
              Отмена
            </Button>
            <Button
              onClick={() => void save().catch(() => undefined)}
              disabled={!canSave}
              loading={create.isPending || update.isPending}
            >
              Сохранить
            </Button>
          </Group>
        </Group>
      </Stack>
    </Modal>
  );
}
