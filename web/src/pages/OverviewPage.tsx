import { BarChart } from '@mantine/charts';
import { Anchor, Group, Paper, Progress, SimpleGrid, Stack, Text } from '@mantine/core';
import dayjs from 'dayjs';
import { Link } from 'react-router-dom';
import { useCurrency, useOverview } from '../api/hooks';
import type { OverviewPeriod } from '../api/types';
import { OrderCard } from '../components/orders';
import { Loading, PageHeader, Profit, Section } from '../components/ui';
import { money, signColor, signedMoney, today } from '../lib/format';

export function OverviewPage() {
  const currency = useCurrency();
  const { data, error } = useOverview(today());

  return (
    <Stack gap="md">
      <PageHeader title="Обзор" />
      {!data ? (
        <Loading error={error} />
      ) : (
        <>
          <SimpleGrid cols={{ base: 1, sm: 3 }} spacing="md">
            {data.periods.map((period) => (
              <PeriodCard key={period.title} period={period} currency={currency} />
            ))}
          </SimpleGrid>

          {data.goal && (
            <Section title="Цель на месяц">
              <Group justify="space-between" mb={6} wrap="wrap">
                <Text>
                  Прибыль <b>{money(data.goal.achieved)}</b> из {money(data.goal.goal)} {currency}
                </Text>
                <Text c="dimmed" size="sm">
                  {data.goal.perDayNeeded !== null
                    ? `Осталось ${money(data.goal.remaining)} — это ${money(data.goal.perDayNeeded)} в день за ${data.goal.daysLeft} дн.`
                    : 'Цель выполнена'}
                </Text>
              </Group>
              <Progress value={Math.min(data.goal.percent, 100)} size="xl" radius="xl" color={data.goal.percent >= 100 ? 'teal' : undefined} />
              <Text size="xs" c="dimmed" mt={4}>
                {data.goal.percent}%
              </Text>
            </Section>
          )}

          <Section
            title="Последние 30 дней"
            aside={
              <Text size="sm" c="dimmed">
                Выручка, затраты и прибыль по дням
              </Text>
            }
          >
            <BarChart
              h={280}
              data={data.chart.map((day) => ({
                day: dayjs(day.date).format('D.MM'),
                Выручка: day.revenue,
                Затраты: day.totalCosts,
                Прибыль: day.profit,
              }))}
              dataKey="day"
              series={[
                { name: 'Выручка', color: 'teal.5' },
                { name: 'Затраты', color: 'gray.5' },
                { name: 'Прибыль', color: 'berry.6' },
              ]}
              tickLine="y"
              gridAxis="y"
              withLegend
              valueFormatter={(value) => money(value)}
              barProps={{ radius: 2 }}
            />
          </Section>

          <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
            <Section
              title="Заказы на неделю"
              aside={
                <Anchor component={Link} to="/orders" size="sm">
                  Все заказы
                </Anchor>
              }
            >
              {data.upcomingOrders.length === 0 ? (
                <Text size="sm" c="dimmed">
                  На ближайшие 7 дней заказов нет.
                </Text>
              ) : (
                <Stack gap="xs">
                  {data.upcomingOrders.map((order) => (
                    <div key={order.id}>
                      <Text size="xs" c="dimmed" mb={2}>
                        {order.dueDate === data.today ? 'Сегодня' : dayjs(order.dueDate).format('D MMMM, dd')}
                      </Text>
                      <OrderCard order={order} actionDate={today()} />
                    </div>
                  ))}
                </Stack>
              )}
            </Section>

            <Stack gap="md">
              <Section title="Долги">
                {data.debtsTotal > 0 ? (
                  <Group justify="space-between">
                    <Text>
                      Вам должны{' '}
                      <Text span fw={700} c="orange.8">
                        {money(data.debtsTotal)}
                      </Text>{' '}
                      {currency}
                      <Text span c="dimmed" size="sm">
                        {' '}
                        ({data.debtorsCount} {data.debtorsCount === 1 ? 'клиент' : 'клиента'})
                      </Text>
                    </Text>
                    <Anchor component={Link} to="/orders?scope=Unpaid" size="sm">
                      Посмотреть
                    </Anchor>
                  </Group>
                ) : (
                  <Text size="sm" c="dimmed">
                    Все заказы оплачены.
                  </Text>
                )}
              </Section>
              <Section title="Склад">
                {data.lowStockCount > 0 ? (
                  <Group justify="space-between">
                    <Text>
                      Заканчиваются продукты: <b>{data.lowStockCount}</b>
                    </Text>
                    <Anchor component={Link} to="/ingredients" size="sm">
                      Посмотреть
                    </Anchor>
                  </Group>
                ) : (
                  <Text size="sm" c="dimmed">
                    Продуктов хватает.
                  </Text>
                )}
              </Section>
            </Stack>
          </SimpleGrid>
        </>
      )}
    </Stack>
  );
}

function Change({ value }: { value: number | null }) {
  if (value === null) return null;
  return (
    <Text span size="xs" fw={600} c={signColor(value)}>
      {value > 0 ? '▲' : value < 0 ? '▼' : ''} {Math.abs(value).toLocaleString('ru-RU')}%
    </Text>
  );
}

function PeriodCard({ period, currency }: { period: OverviewPeriod; currency: string }) {
  const compareTo = period.title === 'Этот месяц' ? 'прошлым месяцем' : 'прошлой неделей';
  return (
    <Paper withBorder p="md" radius="md">
      <Text size="sm" c="dimmed">
        {period.title}
      </Text>
      <Group gap={6} align="baseline" wrap="nowrap">
        <Text fz={26} fw={700} lh={1.2} c={signColor(period.profit)} className="tabular">
          {signedMoney(period.profit)}
        </Text>
        <Text size="sm" c="dimmed">
          {currency} прибыли
        </Text>
        <Change value={period.profitChangePercent} />
      </Group>
      <Text size="sm" mt={4}>
        Выручка {money(period.revenue)} <Change value={period.revenueChangePercent} />
      </Text>
      <Text size="sm" c="dimmed">
        Получено денег {money(period.received)}
      </Text>
      <Text size="xs" c="dimmed" mt={4}>
        Сравнение с {period.title === 'Сегодня' ? 'тем же днём прошлой недели' : `тем же отрезком перед ${compareTo}`}: прибыль{' '}
        <Profit value={period.previousProfit} />
      </Text>
    </Paper>
  );
}
