import { Anchor, Button, Group, SimpleGrid, Stack, Table, Tabs, Text, Tooltip } from '@mantine/core';
import { DatePickerInput } from '@mantine/dates';
import dayjs from 'dayjs';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useCurrency, useMargins, usePeriodReport } from '../api/hooks';
import type { Cost } from '../api/types';
import { EmptyState, Loading, PageHeader, Profit, Section, StatCard } from '../components/ui';
import { addDays, ISO, longDate, money, percent, quantity, today } from '../lib/format';

export function ReportsPage() {
  return (
    <Stack gap="md">
      <PageHeader title="Отчёты" />
      <Tabs defaultValue="period" keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="period">Сводка за период</Tabs.Tab>
          <Tabs.Tab value="margins">Маржинальность изделий</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="period">
          <PeriodReport />
        </Tabs.Panel>
        <Tabs.Panel value="margins">
          <MarginReport />
        </Tabs.Panel>
      </Tabs>
    </Stack>
  );
}

// ---------- Сводка за период ----------

function PeriodReport() {
  const currency = useCurrency();
  const [range, setRange] = useState<[string | null, string | null]>([addDays(today(), -6), today()]);
  const [from, to] = range;
  const { data: report, error } = usePeriodReport(from && to ? from : null, from && to ? to : null);

  const presets: { label: string; range: [string, string] }[] = [
    { label: '7 дней', range: [addDays(today(), -6), today()] },
    { label: '30 дней', range: [addDays(today(), -29), today()] },
    { label: 'Этот месяц', range: [dayjs().startOf('month').format(ISO), today()] },
    {
      label: 'Прошлый месяц',
      range: [
        dayjs().subtract(1, 'month').startOf('month').format(ISO),
        dayjs().subtract(1, 'month').endOf('month').format(ISO),
      ],
    },
  ];

  return (
    <Stack gap="md">
      <Group gap="sm" wrap="wrap">
        <DatePickerInput
          type="range"
          aria-label="Период"
          value={range}
          onChange={setRange}
          valueFormat="D MMMM YYYY"
          w={320}
        />
        {presets.map((preset) => (
          <Button
            key={preset.label}
            variant={from === preset.range[0] && to === preset.range[1] ? 'light' : 'default'}
            onClick={() => setRange(preset.range)}
          >
            {preset.label}
          </Button>
        ))}
      </Group>

      {!report ? (
        from && to ? (
          <Loading error={error} />
        ) : (
          <Text size="sm" c="dimmed">
            Выберите вторую дату периода.
          </Text>
        )
      ) : report.days.length === 0 ? (
        <Section>
          <EmptyState title="За этот период записей нет">
            Здесь появятся итоги по дням, когда вы начнёте записывать приготовленное и проданное в таблицу дня.
          </EmptyState>
        </Section>
      ) : (
        <>
          <SimpleGrid cols={{ base: 2, md: 4 }} spacing="md">
            <StatCard
              label="Затраты"
              value={report.totals.totalCosts}
              currency={currency}
              hint={
                [
                  report.totals.expenses > 0
                    ? `производство ${money(report.totals.productionCost)}, общие расходы ${money(report.totals.expenses)}`
                    : null,
                  report.totals.laborCost > 0 ? `в том числе работа ${money(report.totals.laborCost)}` : null,
                ]
                  .filter(Boolean)
                  .join(' · ') || 'себестоимость приготовленного'
              }
            />
            <StatCard label="Выручка" value={report.totals.revenue} currency={currency} hint="всё, что продано за период" />
            <StatCard
              label="Прибыль"
              value={report.totals.profit}
              currency={currency}
              signed
              hint={`с проданного: ${money(report.totals.profitOnSold)}${report.totals.writeOffLoss > 0 ? ` · списано на ${money(report.totals.writeOffLoss)}` : ''}`}
            />
            <StatCard
              label="Если продать остатки"
              value={report.totals.potentialProfit}
              currency={currency}
              signed
              hint={
                report.totals.unsoldValueAtPrice > 0
                  ? `не продано товара на ${money(report.totals.unsoldValueAtPrice)}`
                  : 'непроданных остатков нет'
              }
            />
          </SimpleGrid>

          <Section title="По дням">
            <div className="table-scroll">
              <Table verticalSpacing="xs" horizontalSpacing="sm" highlightOnHover>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>День</Table.Th>
                    <Table.Th className="num">Затраты на производство</Table.Th>
                    <Table.Th className="num">Общие расходы</Table.Th>
                    <Table.Th className="num">Затраты всего</Table.Th>
                    <Table.Th className="num">Выручка</Table.Th>
                    <Table.Th className="num">Прибыль</Table.Th>
                    <Table.Th className="num">Списано</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {report.days.map((day) => (
                    <Table.Tr key={day.date}>
                      <Table.Td>
                        <Anchor component={Link} to={`/day/${day.date}`} size="sm">
                          {longDate(day.date)}
                        </Anchor>
                      </Table.Td>
                      <Table.Td className="num">{money(day.productionCost)}</Table.Td>
                      <Table.Td className="num">{day.expenses > 0 ? money(day.expenses) : ''}</Table.Td>
                      <Table.Td className="num">{money(day.totalCosts)}</Table.Td>
                      <Table.Td className="num">{money(day.revenue)}</Table.Td>
                      <Table.Td className="num">
                        <Profit value={day.profit} />
                      </Table.Td>
                      <Table.Td className="num">{day.writeOffLoss > 0 ? money(day.writeOffLoss) : ''}</Table.Td>
                    </Table.Tr>
                  ))}
                  <Table.Tr className="total-row">
                    <Table.Td>Итого</Table.Td>
                    <Table.Td className="num">{money(report.totals.productionCost)}</Table.Td>
                    <Table.Td className="num">{report.totals.expenses > 0 ? money(report.totals.expenses) : ''}</Table.Td>
                    <Table.Td className="num">{money(report.totals.totalCosts)}</Table.Td>
                    <Table.Td className="num">{money(report.totals.revenue)}</Table.Td>
                    <Table.Td className="num">
                      <Profit value={report.totals.profit} />
                    </Table.Td>
                    <Table.Td className="num">
                      {report.totals.writeOffLoss > 0 ? money(report.totals.writeOffLoss) : ''}
                    </Table.Td>
                  </Table.Tr>
                </Table.Tbody>
              </Table>
            </div>
          </Section>

          <Section
            title="По изделиям"
            aside={
              <Text size="sm" c="dimmed">
                Что приготовили и что продали за период
              </Text>
            }
          >
            <div className="table-scroll">
              <Table verticalSpacing="xs" horizontalSpacing="sm" highlightOnHover>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>Изделие</Table.Th>
                    <Table.Th className="num">Приготовлено</Table.Th>
                    <Table.Th className="num">Продано</Table.Th>
                    <Table.Th className="num">Списано</Table.Th>
                    <Table.Th className="num">Выручка</Table.Th>
                    <Table.Th className="num">Себестоимость проданного</Table.Th>
                    <Table.Th className="num">Прибыль с проданного</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {report.products.map((product) => (
                    <Table.Tr key={product.productId}>
                      <Table.Td>{product.name}</Table.Td>
                      <Table.Td className="num">{quantity(product.made)}</Table.Td>
                      <Table.Td className="num">{quantity(product.sold)}</Table.Td>
                      <Table.Td className="num">{product.writtenOff > 0 ? quantity(product.writtenOff) : ''}</Table.Td>
                      <Table.Td className="num">{money(product.revenue)}</Table.Td>
                      <Table.Td className="num">{money(product.costOfSold)}</Table.Td>
                      <Table.Td className="num">
                        <Profit value={product.profitOnSold} />
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </div>
          </Section>
        </>
      )}
    </Stack>
  );
}

// ---------- Маржинальность ----------

function costParts(cost: Cost) {
  const parts = [`сырьё ${money(cost.ingredients)}`];
  if (cost.packaging > 0) parts.push(`упаковка ${money(cost.packaging)}`);
  if (cost.labor > 0) parts.push(`работа ${money(cost.labor)}`);
  return parts.join(' · ');
}

function MarginReport() {
  const { data: rows, error } = useMargins();

  if (!rows) return <Loading error={error} />;

  if (rows.length === 0) {
    return (
      <Section>
        <EmptyState title="Изделий пока нет">Добавьте изделия с составом и ценой продажи, и здесь появится их сравнение.</EmptyState>
      </Section>
    );
  }

  return (
    <Section
      aside={
        <Text size="sm" c="dimmed">
          По текущим ценам продуктов и обычным ценам продажи. Сверху самые выгодные.
        </Text>
      }
    >
      <div className="table-scroll">
        <Table verticalSpacing="xs" horizontalSpacing="sm" highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Изделие</Table.Th>
              <Table.Th className="num">С/с за шт</Table.Th>
              <Table.Th className="num">Цена за шт</Table.Th>
              <Table.Th className="num">Прибыль за шт</Table.Th>
              <Table.Th className="num">
                <Tooltip label="Прибыль в процентах от себестоимости">
                  <span>Наценка</span>
                </Tooltip>
              </Table.Th>
              <Table.Th className="num">
                <Tooltip label="Прибыль в процентах от цены">
                  <span>Маржа</span>
                </Tooltip>
              </Table.Th>
              <Table.Th className="num">Партия окупится</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((row) => (
              <Table.Tr key={row.productId}>
                <Table.Td>
                  <Text size="sm" fw={500}>
                    {row.name}
                  </Text>
                </Table.Td>
                <Table.Td className="num">
                  <Tooltip label={costParts(row.unitCostBreakdown)}>
                    <span>{money(row.unitCost)}</span>
                  </Tooltip>
                </Table.Td>
                <Table.Td className="num">{money(row.salePrice)}</Table.Td>
                <Table.Td className="num">
                  <Profit value={row.profitPerUnit} />
                </Table.Td>
                <Table.Td className="num">{percent(row.markupPercent)}</Table.Td>
                <Table.Td className="num">{percent(row.marginPercent)}</Table.Td>
                <Table.Td className="num">
                  {row.breakEvenUnits === null ? (
                    <Text span inherit c="dimmed">
                      нет цены
                    </Text>
                  ) : row.breakEvenUnits > row.yieldQuantity ? (
                    <Text span inherit c="red.7">
                      не окупается
                    </Text>
                  ) : (
                    `${row.breakEvenUnits} из ${quantity(row.yieldQuantity)} шт`
                  )}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </div>
    </Section>
  );
}
