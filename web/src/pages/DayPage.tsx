import {
  ActionIcon,
  Anchor,
  Badge,
  Button,
  Group,
  Menu,
  Modal,
  Select,
  SimpleGrid,
  Stack,
  Table,
  Text,
  TextInput,
  Tooltip,
} from '@mantine/core';
import { DatePickerInput } from '@mantine/dates';
import {
  IconChevronLeft,
  IconChevronRight,
  IconCopy,
  IconDotsVertical,
  IconPlus,
  IconRefresh,
  IconTag,
  IconTrash,
  IconTrashX,
} from '@tabler/icons-react';
import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useAction, useCurrency, useDay, useProducts } from '../api/hooks';
import type { Cost, Day, DayBatch, DayCarryOver, SaleEntry } from '../api/types';
import { InlineNumber, NumberField } from '../components/inputs';
import { OrderCard, OrderEditor } from '../components/orders';
import { EmptyState, Loading, PageHeader, Profit, Section, StatCard, useConfirm } from '../components/ui';
import {
  addDays,
  isIsoDate,
  longDate,
  money,
  quantity,
  shortDate,
  toNumber,
  today,
} from '../lib/format';

/** Что нужно окну продаж и окну списания: подходит и строка дня, и перенесённый остаток. */
interface BatchTarget {
  id: string;
  productName: string;
  unitPrice: number;
  sold: number;
  writtenOff: number;
  remaining: number;
  sales: SaleEntry[];
  writeOffReason: string | null;
}

export function DayPage() {
  const params = useParams();
  const navigate = useNavigate();
  const date = isIsoDate(params.date) ? params.date : today();
  const { data: day, error } = useDay(date);
  const currency = useCurrency();

  const [salesTarget, setSalesTarget] = useState<BatchTarget | null>(null);
  const [writeOffTarget, setWriteOffTarget] = useState<BatchTarget | null>(null);

  const go = (next: string) => navigate(`/day/${next}`);

  // Готовность и оплаты из будущего дня записываются сегодняшним числом.
  const actionDate = date <= today() ? date : today();
  const copyDay = useAction(api.days.copyFrom, 'Строки вчерашнего дня добавлены');

  return (
    <Stack gap="md">
      <PageHeader title={date === today() ? 'Сегодня' : 'День'}>
        <ActionIcon variant="default" size="lg" onClick={() => go(addDays(date, -1))} aria-label="Предыдущий день">
          <IconChevronLeft size={18} />
        </ActionIcon>
        <DatePickerInput
          aria-label="Дата"
          value={date}
          onChange={(value) => value && go(value)}
          valueFormat="D MMMM YYYY, dd"
          w={210}
        />
        <ActionIcon variant="default" size="lg" onClick={() => go(addDays(date, 1))} aria-label="Следующий день">
          <IconChevronRight size={18} />
        </ActionIcon>
        {date !== today() && (
          <Button variant="light" onClick={() => go(today())}>
            Сегодня
          </Button>
        )}
      </PageHeader>

      {!day ? (
        <Loading error={error} />
      ) : (
        <>
          <Summary day={day} currency={currency} />

          <DayOrders day={day} actionDate={actionDate} />

          <Section
            title="Приготовлено за день"
            aside={
              day.batches.every((b) => b.orderId !== null) && (
                <Button
                  variant="subtle"
                  size="compact-sm"
                  leftSection={<IconCopy size={14} />}
                  onClick={() => copyDay.mutate({ date, fromDate: addDays(date, -1) })}
                  loading={copyDay.isPending}
                >
                  Повторить вчерашнее
                </Button>
              )
            }
          >
            {day.batches.length > 0 && (
              <BatchesTable day={day} onSales={setSalesTarget} onWriteOff={setWriteOffTarget} />
            )}
            <AddBatchForm date={date} isFirst={day.batches.length === 0} />
          </Section>

          {day.carryOver.length > 0 && (
            <Section
              title="Остатки прошлых дней"
              aside={
                <Text size="sm" c="dimmed">
                  То, что не продали раньше: можно продать сегодня или списать
                </Text>
              }
            >
              <CarryOverTable day={day} onSales={setSalesTarget} onWriteOff={setWriteOffTarget} />
            </Section>
          )}

          <ExpensesSection day={day} currency={currency} />
        </>
      )}

      {salesTarget && <SalesModal date={date} target={salesTarget} onClose={() => setSalesTarget(null)} />}
      {writeOffTarget && <WriteOffModal date={date} target={writeOffTarget} onClose={() => setWriteOffTarget(null)} />}
    </Stack>
  );
}

// ---------- Итоги дня ----------

function Summary({ day, currency }: { day: Day; currency: string }) {
  const s = day.summary;
  const costParts = [
    s.expenses > 0 ? `производство ${money(s.productionCost)}, общие расходы ${money(s.expenses)}` : null,
    s.laborCost > 0 ? `в том числе работа ${money(s.laborCost)}` : null,
  ].filter(Boolean);
  const cashFromShowcase = s.received - s.receivedFromOrders;

  return (
    <div>
      <SimpleGrid cols={{ base: 2, md: 4 }} spacing="md">
        <StatCard label="Затраты" value={s.totalCosts} currency={currency} hint={costParts.join(' · ') || 'себестоимость приготовленного'} />
        <StatCard label="Выручка" value={s.revenue} currency={currency} hint="продано с витрины и готовые заказы" />
        <StatCard label="Прибыль дня" value={s.profit} currency={currency} signed hint="выручка минус затраты" />
        <StatCard
          label="Получено денег"
          value={s.received}
          currency={currency}
          hint={
            s.receivedFromOrders > 0
              ? `оплаты заказов ${money(s.receivedFromOrders)}, витрина ${money(cashFromShowcase)}`
              : 'продажи с витрины и оплаты заказов'
          }
        />
      </SimpleGrid>

      <Group gap="xl" mt="sm" px={4} wrap="wrap">
        <Text size="sm">
          <Text span c="dimmed">
            Если продать всё:{' '}
          </Text>
          <Profit value={s.potentialProfit} fw={600} />
        </Text>
        <Text size="sm">
          <Text span c="dimmed">
            Прибыль с проданного:{' '}
          </Text>
          <Profit value={s.profitOnSold} fw={600} />
        </Text>
        {s.unsoldValueAtPrice > 0 && (
          <Text size="sm">
            <Text span c="dimmed">
              Не продано на{' '}
            </Text>
            <Text span fw={600}>
              {money(s.unsoldValueAtPrice)}
            </Text>
          </Text>
        )}
        {s.writeOffLoss > 0 && (
          <Text size="sm">
            <Text span c="dimmed">
              Списано на{' '}
            </Text>
            <Text span fw={600} c="red.7">
              {money(s.writeOffLoss)}
            </Text>
          </Text>
        )}
        {s.debtsTotal > 0 && (
          <Anchor component={Link} to="/orders?scope=Unpaid" size="sm">
            Вам должны:{' '}
            <Text span fw={600} c="orange.8">
              {money(s.debtsTotal)}
            </Text>
          </Anchor>
        )}
      </Group>
    </div>
  );
}

// ---------- Заказы на день ----------

function DayOrders({ day, actionDate }: { day: Day; actionDate: string }) {
  const [creating, setCreating] = useState(false);
  return (
    <Section
      title="Заказы и поставки"
      aside={
        <Group gap="sm">
          <Anchor component={Link} to="/orders" size="sm">
            Все заказы
          </Anchor>
          <Button size="compact-md" leftSection={<IconPlus size={16} />} onClick={() => setCreating(true)}>
            Новый заказ
          </Button>
        </Group>
      }
    >
      {day.orders.length === 0 ? (
        <Text size="sm" c="dimmed">
          На этот день заказов нет. Заказы знакомых и поставки в магазин появляются здесь в день выдачи.
        </Text>
      ) : (
        <Stack gap="xs">
          {day.orders.map((order) => (
            <OrderCard key={order.id} order={order} actionDate={actionDate} />
          ))}
        </Stack>
      )}
      {creating && <OrderEditor order={null} defaultDate={day.date} onClose={() => setCreating(false)} />}
    </Section>
  );
}

// ---------- Таблица дня ----------

function costTooltip(cost: Cost) {
  const parts = [`сырьё ${money(cost.ingredients)}`];
  if (cost.packaging > 0) parts.push(`упаковка ${money(cost.packaging)}`);
  if (cost.labor > 0) parts.push(`работа ${money(cost.labor)}`);
  return parts.join(' · ');
}

function OrderBadge({ number, shop }: { number: number | null; shop?: boolean }) {
  if (number === null) return null;
  if (shop) {
    return (
      <Badge size="sm" variant="light" color="grape" component={Link} to="/orders" style={{ cursor: 'pointer', flexShrink: 0 }}>
        Магазин №{number}
      </Badge>
    );
  }
  return (
    <Badge size="sm" variant="light" color="blue" component={Link} to="/orders" style={{ cursor: 'pointer', flexShrink: 0 }}>
      Заказ №{number}
    </Badge>
  );
}

function BatchesTable({
  day,
  onSales,
  onWriteOff,
}: {
  day: Day;
  onSales: (target: BatchTarget) => void;
  onWriteOff: (target: BatchTarget) => void;
}) {
  const confirm = useConfirm();
  const update = useAction(api.batches.update);
  const remove = useAction(api.batches.remove);
  const recalculate = useAction(api.batches.recalculateCost, 'Себестоимость пересчитана по текущим ценам');

  const rows = day.batches;
  const sum = (pick: (b: DayBatch) => number) => rows.reduce((total, b) => total + pick(b), 0);

  const removeBatch = async (batch: DayBatch) => {
    const ok = await confirm({
      title: 'Удалить строку?',
      message: `«${batch.productName}», ${quantity(batch.quantity)} шт. Продажи и списания по этой строке тоже удалятся, сырьё вернётся на склад.`,
      confirmLabel: 'Удалить',
      danger: true,
    });
    if (ok) remove.mutate(batch.id);
  };

  return (
    <div className="table-scroll">
      <Table verticalSpacing={6} horizontalSpacing="xs" highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Изделие</Table.Th>
            <Table.Th className="num">Кол‑во</Table.Th>
            <Table.Th className="num"><span style={{ whiteSpace: 'nowrap' }}>С/с</span> за шт</Table.Th>
            <Table.Th className="num">Цена за шт</Table.Th>
            <Table.Th className="num">Продано</Table.Th>
            <Table.Th className="num">Списано</Table.Th>
            <Table.Th className="num">Остаток</Table.Th>
            <Table.Th className="num">Затраты</Table.Th>
            <Table.Th className="num">Потенц. выручка</Table.Th>
            <Table.Th className="num">Потенц. прибыль</Table.Th>
            <Table.Th className="num">Факт. выручка</Table.Th>
            <Table.Th />
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {rows.map((batch) => {
            const locked = batch.orderId !== null;
            const later = [
              batch.soldLater > 0 ? `продано ${quantity(batch.soldLater)}` : null,
              batch.writtenOffLater > 0 ? `списано ${quantity(batch.writtenOffLater)}` : null,
            ].filter(Boolean);

            return (
              <Table.Tr key={batch.id}>
                <Table.Td className="name-cell">
                  <Group gap={6} wrap="nowrap">
                    <Text fw={500} size="sm">
                      {batch.productName}
                    </Text>
                    <OrderBadge number={batch.orderNumber} shop={batch.orderIsConsignment} />
                  </Group>
                  {batch.orderReturned > 0 && (
                    <Text size="xs" c="grape.7">
                      Магазин вернул {quantity(batch.orderReturned)}
                    </Text>
                  )}
                  {later.length > 0 && (
                    <Text size="xs" c="dimmed">
                      Позже: {later.join(', ')}
                    </Text>
                  )}
                </Table.Td>
                <Table.Td className="num">
                  <Cell locked={locked} value={quantity(batch.quantity)}>
                    <InlineNumber
                      label="Количество"
                      value={batch.quantity}
                      onCommit={(value) =>
                        update.mutateAsync({ id: batch.id, quantity: value, unitPrice: batch.unitPrice })
                      }
                    />
                  </Cell>
                </Table.Td>
                <Table.Td className="num">
                  <Tooltip label={costTooltip(batch.unitCostBreakdown)}>
                    <span>{money(batch.unitCost)}</span>
                  </Tooltip>
                </Table.Td>
                <Table.Td className="num">
                  <Cell locked={locked} value={money(batch.unitPrice)}>
                    <InlineNumber
                      label="Цена за штуку"
                      value={batch.unitPrice}
                      onCommit={(value) =>
                        update.mutateAsync({ id: batch.id, quantity: batch.quantity, unitPrice: value })
                      }
                    />
                  </Cell>
                </Table.Td>
                <Table.Td className="num">
                  <Cell locked={locked} value={quantity(batch.sold)}>
                    <SoldCell date={day.date} target={batch} onOpen={() => onSales(batch)} />
                  </Cell>
                </Table.Td>
                <Table.Td className="num">
                  <Cell locked={locked} value={quantity(batch.writtenOff)}>
                    <WrittenOffCell date={day.date} target={batch} />
                  </Cell>
                </Table.Td>
                <Table.Td className="num">
                  <Text span inherit fw={batch.remaining > 0 ? 600 : undefined} c={batch.remaining > 0 ? 'orange.8' : 'dimmed'}>
                    {quantity(batch.remaining)}
                  </Text>
                </Table.Td>
                <Table.Td className="num">
                  <Tooltip
                    label={
                      batch.breakEvenUnits !== null
                        ? `Окупится, если продать ${batch.breakEvenUnits} из ${quantity(batch.quantity)}`
                        : 'Укажите цену, чтобы увидеть окупаемость'
                    }
                  >
                    <span>{money(batch.totalCost)}</span>
                  </Tooltip>
                </Table.Td>
                <Table.Td className="num">{money(batch.potentialRevenue)}</Table.Td>
                <Table.Td className="num">
                  <Profit value={batch.potentialProfit} />
                </Table.Td>
                <Table.Td className="num">{money(batch.actualRevenue)}</Table.Td>
                <Table.Td>
                  {!locked && (
                    <Menu position="bottom-end" withinPortal>
                      <Menu.Target>
                        <ActionIcon variant="subtle" color="gray" aria-label={`Действия: ${batch.productName}`}>
                          <IconDotsVertical size={16} />
                        </ActionIcon>
                      </Menu.Target>
                      <Menu.Dropdown>
                        <Menu.Item leftSection={<IconTag size={16} />} onClick={() => onSales(batch)}>
                          Продать по другой цене
                        </Menu.Item>
                        <Menu.Item leftSection={<IconTrashX size={16} />} onClick={() => onWriteOff(batch)}>
                          Списать с причиной
                        </Menu.Item>
                        <Menu.Item leftSection={<IconRefresh size={16} />} onClick={() => recalculate.mutate(batch.id)}>
                          Пересчитать себестоимость
                        </Menu.Item>
                        <Menu.Divider />
                        <Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => removeBatch(batch)}>
                          Удалить строку
                        </Menu.Item>
                      </Menu.Dropdown>
                    </Menu>
                  )}
                </Table.Td>
              </Table.Tr>
            );
          })}
          <Table.Tr className="total-row">
            <Table.Td>Итого</Table.Td>
            <Table.Td />
            <Table.Td />
            <Table.Td />
            <Table.Td className="num">{quantity(sum((b) => b.sold))}</Table.Td>
            <Table.Td className="num">{quantity(sum((b) => b.writtenOff))}</Table.Td>
            <Table.Td className="num">{quantity(sum((b) => b.remaining))}</Table.Td>
            <Table.Td className="num">{money(sum((b) => b.totalCost))}</Table.Td>
            <Table.Td className="num">{money(sum((b) => b.potentialRevenue))}</Table.Td>
            <Table.Td className="num">
              <Profit value={sum((b) => b.potentialProfit)} />
            </Table.Td>
            <Table.Td className="num">{money(sum((b) => b.actualRevenue))}</Table.Td>
            <Table.Td />
          </Table.Tr>
        </Table.Tbody>
      </Table>
    </div>
  );
}

/** Партии под заказ не редактируются в таблице дня: показываем просто число. */
function Cell({ locked, value, children }: { locked: boolean; value: string; children: React.ReactNode }) {
  return locked ? <>{value}</> : <Group justify="flex-end">{children}</Group>;
}

function SoldCell({ date, target, onOpen }: { date: string; target: BatchTarget; onOpen: () => void }) {
  const setSales = useAction(api.batches.setSales);
  const atRegularPrice =
    target.sales.length === 0 || (target.sales.length === 1 && target.sales[0].unitPrice === target.unitPrice);

  if (!atRegularPrice) {
    return (
      <Tooltip label="Продано по разным ценам. Нажмите, чтобы изменить">
        <Button variant="light" size="compact-sm" onClick={onOpen} className="tabular">
          {quantity(target.sold)}
        </Button>
      </Tooltip>
    );
  }

  return (
    <InlineNumber
      label="Продано"
      value={target.sold}
      onCommit={(value) =>
        setSales.mutateAsync({
          id: target.id,
          date,
          entries: value > 0 ? [{ quantity: value, unitPrice: target.unitPrice }] : [],
        })
      }
    />
  );
}

function WrittenOffCell({ date, target }: { date: string; target: BatchTarget }) {
  const setWriteOff = useAction(api.batches.setWriteOff);
  return (
    <Tooltip label={target.writeOffReason ?? ''} disabled={!target.writeOffReason}>
      <div>
        <InlineNumber
          label="Списано"
          value={target.writtenOff}
          onCommit={(value) =>
            setWriteOff.mutateAsync({ id: target.id, date, quantity: value, reason: target.writeOffReason })
          }
        />
      </div>
    </Tooltip>
  );
}

// ---------- Новая строка ----------

function AddBatchForm({ date, isFirst }: { date: string; isFirst: boolean }) {
  const { data: products } = useProducts(false);
  const create = useAction(api.batches.create);
  const productRef = useRef<HTMLInputElement>(null);

  const [productId, setProductId] = useState<string | null>(null);
  const [made, setMade] = useState<number | string>('');
  const [price, setPrice] = useState<number | string>('');
  const [sold, setSold] = useState<number | string>('');

  // При переходе на другой день форма очищается.
  useEffect(() => {
    setProductId(null);
    setMade('');
    setPrice('');
    setSold('');
  }, [date]);

  if (!products) return null;

  const finished = products.filter((p) => p.type === 'Finished');
  if (finished.length === 0) {
    return (
      <EmptyState title="Сначала добавьте изделия">
        Заведите продукты с ценами и изделия с составом — тогда здесь можно будет записывать, что приготовили и
        сколько продали, а программа посчитает себестоимость и прибыль.
        <br />
        <Button component={Link} to="/products" mt="md" variant="light">
          Перейти к изделиям
        </Button>
      </EmptyState>
    );
  }

  const selected = finished.find((p) => p.id === productId);
  const madeNumber = toNumber(made);
  const canSubmit = selected !== undefined && madeNumber !== null && madeNumber > 0;

  const submit = async () => {
    if (!canSubmit) return;
    await create.mutateAsync({
      date,
      productId: selected.id,
      quantity: madeNumber,
      unitPrice: toNumber(price),
      soldQuantity: toNumber(sold),
    });
    setProductId(null);
    setMade('');
    setPrice('');
    setSold('');
    productRef.current?.focus();
  };

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        void submit().catch(() => undefined);
      }}
    >
      <Group align="flex-end" gap="sm" mt={isFirst ? 0 : 'md'} wrap="wrap">
        <Select
          ref={productRef}
          label={isFirst ? 'Что приготовили' : 'Добавить изделие'}
          placeholder="Выберите изделие"
          data={finished.map((p) => ({ value: p.id, label: p.name }))}
          value={productId}
          onChange={(value) => {
            setProductId(value);
            const product = finished.find((p) => p.id === value);
            setPrice(product ? product.salePrice : '');
          }}
          searchable
          nothingFoundMessage="Такого изделия нет"
          w={280}
        />
        <NumberField label="Приготовили, шт" value={made} onChange={setMade} w={130} />
        <NumberField label="Цена за шт" value={price} onChange={setPrice} w={110} />
        <NumberField label="Продали, шт" placeholder="можно позже" value={sold} onChange={setSold} w={130} />
        <Button type="submit" leftSection={<IconPlus size={16} />} disabled={!canSubmit} loading={create.isPending}>
          Добавить
        </Button>
      </Group>
      {selected?.unitCost && (
        <Text size="xs" c="dimmed" mt={6}>
          Себестоимость {money(selected.unitCost.total)} за штуку ({costTooltip(selected.unitCost)}), обычная цена{' '}
          {money(selected.salePrice)}
        </Text>
      )}
      {selected?.costError && (
        <Text size="xs" c="red.7" mt={6}>
          {selected.costError}
        </Text>
      )}
    </form>
  );
}

// ---------- Остатки прошлых дней ----------

function CarryOverTable({
  day,
  onSales,
  onWriteOff,
}: {
  day: Day;
  onSales: (target: BatchTarget) => void;
  onWriteOff: (target: BatchTarget) => void;
}) {
  const setWriteOff = useAction(api.batches.setWriteOff);
  const rows = day.carryOver;
  const sum = (pick: (b: DayCarryOver) => number) => rows.reduce((total, b) => total + pick(b), 0);

  return (
    <div className="table-scroll">
      <Table verticalSpacing={6} horizontalSpacing="xs" highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Изделие</Table.Th>
            <Table.Th>Приготовлено</Table.Th>
            <Table.Th className="num"><span style={{ whiteSpace: 'nowrap' }}>С/с</span> за шт</Table.Th>
            <Table.Th className="num">Цена за шт</Table.Th>
            <Table.Th className="num">Было утром</Table.Th>
            <Table.Th className="num">Продано сегодня</Table.Th>
            <Table.Th className="num">Списано сегодня</Table.Th>
            <Table.Th className="num">Остаток</Table.Th>
            <Table.Th className="num">Выручка</Table.Th>
            <Table.Th />
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {rows.map((batch) => {
            const locked = false;
            return (
              <Table.Tr key={batch.id}>
                <Table.Td className="name-cell">
                  <Group gap={6} wrap="nowrap">
                    <Text fw={500} size="sm">
                      {batch.productName}
                    </Text>
                    <OrderBadge number={batch.orderNumber} shop={batch.orderIsConsignment} />
                  </Group>
                </Table.Td>
                <Table.Td>
                  <Anchor component={Link} to={`/day/${batch.productionDate}`} size="sm">
                    {longDate(batch.productionDate)}
                  </Anchor>
                </Table.Td>
                <Table.Td className="num">{money(batch.unitCost)}</Table.Td>
                <Table.Td className="num">{money(batch.unitPrice)}</Table.Td>
                <Table.Td className="num">{quantity(batch.remainingAtStart)}</Table.Td>
                <Table.Td className="num">
                  <Cell locked={locked} value={quantity(batch.sold)}>
                    <SoldCell date={day.date} target={batch} onOpen={() => onSales(batch)} />
                  </Cell>
                </Table.Td>
                <Table.Td className="num">
                  <Cell locked={locked} value={quantity(batch.writtenOff)}>
                    <WrittenOffCell date={day.date} target={batch} />
                  </Cell>
                </Table.Td>
                <Table.Td className="num">
                  <Text span inherit fw={batch.remaining > 0 ? 600 : undefined} c={batch.remaining > 0 ? 'orange.8' : 'dimmed'}>
                    {quantity(batch.remaining)}
                  </Text>
                </Table.Td>
                <Table.Td className="num">{money(batch.revenue)}</Table.Td>
                <Table.Td>
                  {!locked && (
                    <Menu position="bottom-end" withinPortal>
                      <Menu.Target>
                        <ActionIcon variant="subtle" color="gray" aria-label={`Действия: ${batch.productName}`}>
                          <IconDotsVertical size={16} />
                        </ActionIcon>
                      </Menu.Target>
                      <Menu.Dropdown>
                        <Menu.Item leftSection={<IconTag size={16} />} onClick={() => onSales(batch)}>
                          Продать по другой цене
                        </Menu.Item>
                        <Menu.Item leftSection={<IconTrashX size={16} />} onClick={() => onWriteOff(batch)}>
                          Списать с причиной
                        </Menu.Item>
                        {batch.remaining > 0 && (
                          <Menu.Item
                            color="red"
                            leftSection={<IconTrash size={16} />}
                            onClick={() =>
                              setWriteOff.mutate({
                                id: batch.id,
                                date: day.date,
                                quantity: batch.writtenOff + batch.remaining,
                                reason: batch.writeOffReason,
                              })
                            }
                          >
                            Списать весь остаток ({quantity(batch.remaining)})
                          </Menu.Item>
                        )}
                      </Menu.Dropdown>
                    </Menu>
                  )}
                </Table.Td>
              </Table.Tr>
            );
          })}
          <Table.Tr className="total-row">
            <Table.Td colSpan={5}>Итого</Table.Td>
            <Table.Td className="num">{quantity(sum((b) => b.sold))}</Table.Td>
            <Table.Td className="num">{quantity(sum((b) => b.writtenOff))}</Table.Td>
            <Table.Td className="num">{quantity(sum((b) => b.remaining))}</Table.Td>
            <Table.Td className="num">{money(sum((b) => b.revenue))}</Table.Td>
            <Table.Td />
          </Table.Tr>
        </Table.Tbody>
      </Table>
    </div>
  );
}

// ---------- Общие расходы ----------

function ExpensesSection({ day, currency }: { day: Day; currency: string }) {
  const create = useAction(api.expenses.create);
  const remove = useAction(api.expenses.remove);
  const [name, setName] = useState('');
  const [amount, setAmount] = useState<number | string>('');

  const amountNumber = toNumber(amount);
  const canSubmit = name.trim() !== '' && amountNumber !== null && amountNumber > 0;

  const submit = async () => {
    if (!canSubmit) return;
    await create.mutateAsync({ date: day.date, name: name.trim(), amount: amountNumber });
    setName('');
    setAmount('');
  };

  return (
    <Section
      title="Общие расходы дня"
      aside={
        day.expenses.length > 0 && (
          <Text size="sm" fw={600}>
            {money(day.summary.expenses)} {currency}
          </Text>
        )
      }
    >
      {day.expenses.length === 0 ? (
        <Text size="sm" c="dimmed" mb="sm">
          Такси, аренда места, газ — всё, что потрачено за день, но не относится к конкретному изделию.
        </Text>
      ) : (
        <Table verticalSpacing={6} mb="sm">
          <Table.Tbody>
            {day.expenses.map((expense) => (
              <Table.Tr key={expense.id}>
                <Table.Td>{expense.name}</Table.Td>
                <Table.Td className="num">{money(expense.amount)}</Table.Td>
                <Table.Td w={40}>
                  <ActionIcon
                    variant="subtle"
                    color="gray"
                    onClick={() => remove.mutate(expense.id)}
                    aria-label={`Удалить расход: ${expense.name}`}
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
          void submit().catch(() => undefined);
        }}
      >
        <Group align="flex-end" gap="sm" wrap="wrap">
          <TextInput
            label="На что потратили"
            placeholder="Например, такси"
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
            style={{ flex: 1, minWidth: 160 }}
          />
          <NumberField label="Сумма" value={amount} onChange={setAmount} w={110} />
          <Button type="submit" variant="light" disabled={!canSubmit} loading={create.isPending}>
            Добавить
          </Button>
        </Group>
      </form>
    </Section>
  );
}

// ---------- Окно: продажа по разным ценам ----------

function SalesModal({ date, target, onClose }: { date: string; target: BatchTarget; onClose: () => void }) {
  const setSales = useAction(api.batches.setSales);
  const [entries, setEntries] = useState<{ quantity: number | string; unitPrice: number | string }[]>(
    target.sales.length > 0
      ? target.sales.map((s) => ({ quantity: s.quantity, unitPrice: s.unitPrice }))
      : [{ quantity: '', unitPrice: target.unitPrice }],
  );

  const available = target.sold + target.remaining;
  const parsed = entries.map((e) => ({ quantity: toNumber(e.quantity) ?? 0, unitPrice: toNumber(e.unitPrice) ?? 0 }));
  const totalQuantity = parsed.reduce((total, e) => total + e.quantity, 0);
  const totalAmount = parsed.reduce((total, e) => total + e.quantity * e.unitPrice, 0);

  const change = (index: number, patch: Partial<(typeof entries)[number]>) =>
    setEntries(entries.map((entry, i) => (i === index ? { ...entry, ...patch } : entry)));

  const save = async () => {
    await setSales.mutateAsync({ id: target.id, date, entries: parsed.filter((e) => e.quantity > 0) });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={`Продажи: ${target.productName}`} centered>
      <Text size="sm" c="dimmed" mb="sm">
        {shortDate(date)}. Можно записать несколько продаж по разным ценам, например часть со скидкой вечером.
        Доступно {quantity(available)} шт.
      </Text>
      <Stack gap="xs">
        {entries.map((entry, index) => (
          <Group key={index} gap="sm" align="flex-end" wrap="nowrap">
            <NumberField
              label={index === 0 ? 'Продано, шт' : undefined}
              aria-label="Продано, шт"
              value={entry.quantity}
              onChange={(value) => change(index, { quantity: value })}
              style={{ flex: 1 }}
              data-autofocus={index === 0 || undefined}
            />
            <NumberField
              label={index === 0 ? 'Цена за шт' : undefined}
              aria-label="Цена за шт"
              value={entry.unitPrice}
              onChange={(value) => change(index, { unitPrice: value })}
              style={{ flex: 1 }}
            />
            <ActionIcon
              variant="subtle"
              color="gray"
              size="lg"
              onClick={() => setEntries(entries.filter((_, i) => i !== index))}
              disabled={entries.length === 1}
              aria-label="Убрать продажу"
            >
              <IconTrash size={16} />
            </ActionIcon>
          </Group>
        ))}
      </Stack>
      <Button
        variant="subtle"
        size="compact-sm"
        mt="xs"
        leftSection={<IconPlus size={14} />}
        onClick={() => setEntries([...entries, { quantity: '', unitPrice: target.unitPrice }])}
      >
        Ещё одна цена
      </Button>

      <Group justify="space-between" mt="md">
        <Text size="sm" c={totalQuantity > available ? 'red.7' : undefined}>
          Всего {quantity(totalQuantity)} шт на {money(totalAmount)}
        </Text>
        <Group gap="sm">
          <Button variant="default" onClick={onClose}>
            Отмена
          </Button>
          <Button onClick={() => void save().catch(() => undefined)} loading={setSales.isPending} disabled={totalQuantity > available}>
            Сохранить
          </Button>
        </Group>
      </Group>
    </Modal>
  );
}

// ---------- Окно: списание с причиной ----------

function WriteOffModal({ date, target, onClose }: { date: string; target: BatchTarget; onClose: () => void }) {
  const setWriteOff = useAction(api.batches.setWriteOff);
  const available = target.writtenOff + target.remaining;
  const [amount, setAmount] = useState<number | string>(target.writtenOff > 0 ? target.writtenOff : available);
  const [reason, setReason] = useState(target.writeOffReason ?? '');

  const amountNumber = toNumber(amount) ?? 0;

  const save = async () => {
    await setWriteOff.mutateAsync({ id: target.id, date, quantity: amountNumber, reason: reason.trim() || null });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={`Списание: ${target.productName}`} centered size="sm">
      <Text size="sm" c="dimmed" mb="sm">
        {shortDate(date)}. Списанное не продаётся и считается потерей по себестоимости. Можно списать до{' '}
        {quantity(available)} шт.
      </Text>
      <Stack gap="sm">
        <NumberField label="Списать, шт" value={amount} onChange={setAmount} data-autofocus />
        <TextInput
          label="Причина"
          placeholder="Испортилось, отдали бесплатно…"
          value={reason}
          onChange={(event) => setReason(event.currentTarget.value)}
        />
      </Stack>
      <Group justify="flex-end" gap="sm" mt="md">
        <Button variant="default" onClick={onClose}>
          Отмена
        </Button>
        <Button
          color="red"
          onClick={() => void save().catch(() => undefined)}
          loading={setWriteOff.isPending}
          disabled={amountNumber > available}
        >
          {amountNumber === 0 ? 'Убрать списание' : 'Списать'}
        </Button>
      </Group>
    </Modal>
  );
}
