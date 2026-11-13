import { Alert, Button, Group, SegmentedControl, Stack, Table, Text } from '@mantine/core';
import { IconPlus } from '@tabler/icons-react';
import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useCurrency, useOrders } from '../api/hooks';
import type { OrderScope } from '../api/types';
import { OrderActions, OrderEditor, OrderStatusBadge, ShopBadge } from '../components/orders';
import { EmptyState, Loading, PageHeader, Profit, Section } from '../components/ui';
import { money, quantity, shortDate, shortTime, today } from '../lib/format';

export function OrdersPage() {
  const currency = useCurrency();
  const [searchParams] = useSearchParams();
  const initialScope = searchParams.get('scope');
  const [scope, setScope] = useState<OrderScope>(
    initialScope === 'Unpaid' || initialScope === 'All' ? initialScope : 'Active',
  );
  const { data: orders, error } = useOrders({ scope });
  const [creating, setCreating] = useState(false);

  const debt = scope === 'Unpaid' ? (orders ?? []).reduce((sum, o) => sum + o.balance, 0) : 0;

  return (
    <Stack gap="md">
      <PageHeader title="Заказы">
        <SegmentedControl
          value={scope}
          onChange={(value) => setScope(value as OrderScope)}
          data={[
            { value: 'Active', label: 'Активные' },
            { value: 'Unpaid', label: 'Ждут оплаты' },
            { value: 'All', label: 'Все' },
          ]}
        />
        <Button leftSection={<IconPlus size={16} />} onClick={() => setCreating(true)}>
          Новый заказ
        </Button>
      </PageHeader>

      {scope === 'Unpaid' && orders && orders.length > 0 && (
        <Alert color="orange" variant="light">
          Вам должны <b>{money(debt)}</b> {currency} по {orders.length} заказам. Когда деньги придут, нажмите «Оплата».
        </Alert>
      )}

      {!orders ? (
        <Loading error={error} />
      ) : orders.length === 0 ? (
        <Section>
          <EmptyState
            title={scope === 'Active' ? 'Активных заказов нет' : scope === 'Unpaid' ? 'Никто ничего не должен' : 'Заказов пока нет'}
          >
            Запишите заказ или поставку в магазин. Когда заказ готов, его затраты и выручка попадают в таблицу этого дня,
            а оплату можно внести позже.
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
                  const active = order.status !== 'Delivered' && order.status !== 'Cancelled';
                  const overdue = active && order.dueDate < today();
                  const returned = order.items.filter((i) => i.returned > 0);
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
                        {order.producedDate && (
                          <Text size="xs" c="dimmed">
                            готов {shortDate(order.producedDate)}
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td>
                        <Group gap={6} wrap="nowrap">
                          <Text size="sm" fw={500}>
                            {order.customerName}
                          </Text>
                          {order.isConsignment && <ShopBadge />}
                        </Group>
                        {order.customerPhone && (
                          <Text size="xs" c="dimmed">
                            {order.customerPhone}
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm">{order.itemsSummary}</Text>
                        {returned.length > 0 && (
                          <Text size="xs" c="grape.7">
                            Возврат: {returned.map((i) => `${i.productName} × ${quantity(i.returned)}`).join(', ')}
                          </Text>
                        )}
                        {order.note && (
                          <Text size="xs" c="dimmed">
                            {order.note}
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td className="num">
                        {money(order.total)}
                        {order.profit !== null && order.status !== 'Cancelled' && (
                          <Text size="xs" c="dimmed">
                            прибыль <Profit value={order.profit} />
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td className="num">
                        {order.status === 'Cancelled' ? (
                          ''
                        ) : order.balance > 0 ? (
                          <Text span inherit fw={600} c={order.status === 'Delivered' ? 'orange.8' : undefined}>
                            {money(order.balance)}
                          </Text>
                        ) : order.balance < 0 ? (
                          <Text span inherit c="dimmed">
                            переплата {money(-order.balance)}
                          </Text>
                        ) : (
                          <Text span inherit c="teal.8">
                            оплачен
                          </Text>
                        )}
                        {order.paid > 0 && order.balance > 0 && (
                          <Text size="xs" c="dimmed">
                            оплачено {money(order.paid)}
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td>
                        <OrderStatusBadge order={order} />
                      </Table.Td>
                      <Table.Td>
                        <OrderActions order={order} actionDate={today()} compact />
                      </Table.Td>
                    </Table.Tr>
                  );
                })}
              </Table.Tbody>
            </Table>
          </div>
        </Section>
      )}

      {creating && <OrderEditor order={null} onClose={() => setCreating(false)} />}
    </Stack>
  );
}
