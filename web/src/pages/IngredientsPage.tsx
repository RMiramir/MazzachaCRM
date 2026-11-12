import {
  ActionIcon,
  Badge,
  Button,
  Checkbox,
  Group,
  Menu,
  Modal,
  SegmentedControl,
  Select,
  Stack,
  Table,
  Tabs,
  Text,
  TextInput,
} from '@mantine/core';
import { DatePickerInput } from '@mantine/dates';
import {
  IconAdjustments,
  IconDotsVertical,
  IconHistory,
  IconPencil,
  IconPlus,
  IconSearch,
  IconShoppingCart,
  IconTrash,
  IconTrashX,
} from '@tabler/icons-react';
import { useState } from 'react';
import { api } from '../api/client';
import { useAction, useCurrency, useIngredients, useStockMovements } from '../api/hooks';
import type { Ingredient, IngredientKind, MeasureUnit } from '../api/types';
import { NumberField } from '../components/inputs';
import { EmptyState, Loading, PageHeader, Section, useConfirm } from '../components/ui';
import { kindLabel, money, movementLabel, quantity, shortDate, toNumber, today, unitLabel } from '../lib/format';

type Dialog =
  | { kind: 'form'; ingredient: Ingredient | null }
  | { kind: 'purchase' | 'adjust' | 'writeOff'; ingredient: Ingredient };

export function IngredientsPage() {
  const { data: ingredients, error } = useIngredients();
  const [tab, setTab] = useState<string | null>('list');
  const [journalIngredient, setJournalIngredient] = useState<string | null>(null);
  const [dialog, setDialog] = useState<Dialog | null>(null);

  const openHistory = (ingredient: Ingredient) => {
    setJournalIngredient(ingredient.id);
    setTab('journal');
  };

  return (
    <Stack gap="md">
      <PageHeader title="Продукты и склад">
        <Button leftSection={<IconPlus size={16} />} onClick={() => setDialog({ kind: 'form', ingredient: null })}>
          Новый продукт
        </Button>
      </PageHeader>

      <Tabs value={tab} onChange={setTab}>
        <Tabs.List mb="md">
          <Tabs.Tab value="list">Продукты и упаковка</Tabs.Tab>
          <Tabs.Tab value="journal">Журнал склада</Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="list">
          {!ingredients ? (
            <Loading error={error} />
          ) : (
            <IngredientsList ingredients={ingredients} onDialog={setDialog} onHistory={openHistory} />
          )}
        </Tabs.Panel>

        <Tabs.Panel value="journal">
          <Journal
            ingredients={ingredients ?? []}
            ingredientId={journalIngredient}
            onIngredientChange={setJournalIngredient}
          />
        </Tabs.Panel>
      </Tabs>

      {dialog?.kind === 'form' && <IngredientForm ingredient={dialog.ingredient} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'purchase' && <PurchaseModal ingredient={dialog.ingredient} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'adjust' && <AdjustModal ingredient={dialog.ingredient} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'writeOff' && <StockWriteOffModal ingredient={dialog.ingredient} onClose={() => setDialog(null)} />}
    </Stack>
  );
}

/** Цена за привычную единицу: килограмм, литр или штуку. */
function pricePerUnit(ingredient: Ingredient) {
  if (ingredient.unit === 'Gram') return `${money(ingredient.unitPrice * 1000)} за кг`;
  if (ingredient.unit === 'Milliliter') return `${money(ingredient.unitPrice * 1000)} за л`;
  return `${money(ingredient.unitPrice)} за шт`;
}

function IngredientsList({
  ingredients,
  onDialog,
  onHistory,
}: {
  ingredients: Ingredient[];
  onDialog: (dialog: Dialog) => void;
  onHistory: (ingredient: Ingredient) => void;
}) {
  const currency = useCurrency();
  const confirm = useConfirm();
  const remove = useAction(api.ingredients.remove, 'Продукт удалён');
  const [filter, setFilter] = useState('all');
  const [search, setSearch] = useState('');

  if (ingredients.length === 0) {
    return (
      <Section>
        <EmptyState title="Продуктов пока нет">
          Добавьте продукты и упаковку с ценой за определённый вес или количество, например «Мука — 8 {currency} за
          1000 г». Из них потом составляются изделия.
        </EmptyState>
      </Section>
    );
  }

  const visible = ingredients.filter(
    (i) =>
      (filter === 'all' || i.kind === filter || (filter === 'low' && (i.isLowStock || i.isOutOfStock))) &&
      i.name.toLowerCase().includes(search.trim().toLowerCase()),
  );
  const stockValue = ingredients.reduce((total, i) => total + i.stockValue, 0);
  const lowCount = ingredients.filter((i) => i.isLowStock || i.isOutOfStock).length;

  const removeIngredient = async (ingredient: Ingredient) => {
    const ok = await confirm({
      title: 'Удалить продукт?',
      message: `«${ingredient.name}» будет удалён вместе с историей склада.`,
      confirmLabel: 'Удалить',
      danger: true,
    });
    if (ok) remove.mutate(ingredient.id);
  };

  return (
    <Section>
      <Group justify="space-between" mb="sm" wrap="wrap" gap="sm">
        <Group gap="sm" wrap="wrap">
          <SegmentedControl
            value={filter}
            onChange={setFilter}
            data={[
              { value: 'all', label: 'Все' },
              { value: 'Ingredient', label: 'Продукты' },
              { value: 'Packaging', label: 'Упаковка' },
              { value: 'low', label: lowCount > 0 ? `Заканчиваются (${lowCount})` : 'Заканчиваются' },
            ]}
          />
          <TextInput
            aria-label="Поиск"
            placeholder="Поиск по названию"
            leftSection={<IconSearch size={16} />}
            value={search}
            onChange={(event) => setSearch(event.currentTarget.value)}
            w={220}
          />
        </Group>
        <Text size="sm" c="dimmed">
          На складе на {money(stockValue)} {currency}
        </Text>
      </Group>

      <div className="table-scroll">
        <Table verticalSpacing="xs" horizontalSpacing="sm" highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Название</Table.Th>
              <Table.Th className="num">Цена</Table.Th>
              <Table.Th className="num">За единицу</Table.Th>
              <Table.Th className="num">Остаток</Table.Th>
              <Table.Th className="num">Стоимость остатка</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {visible.map((ingredient) => (
              <Table.Tr key={ingredient.id}>
                <Table.Td>
                  <Group gap={8} wrap="nowrap">
                    <Text fw={500} size="sm">
                      {ingredient.name}
                    </Text>
                    {ingredient.kind === 'Packaging' && (
                      <Badge size="xs" variant="light" color="gray">
                        Упаковка
                      </Badge>
                    )}
                  </Group>
                  {ingredient.usedInProducts > 0 && (
                    <Text size="xs" c="dimmed">
                      В составе изделий: {ingredient.usedInProducts}
                    </Text>
                  )}
                </Table.Td>
                <Table.Td className="num">
                  {money(ingredient.packagePrice)} за {quantity(ingredient.packageSize)} {unitLabel[ingredient.unit]}
                </Table.Td>
                <Table.Td className="num">
                  <Text span inherit c="dimmed">
                    {pricePerUnit(ingredient)}
                  </Text>
                </Table.Td>
                <Table.Td className="num">
                  <StockCell ingredient={ingredient} />
                </Table.Td>
                <Table.Td className="num">{money(ingredient.stockValue)}</Table.Td>
                <Table.Td>
                  <Group gap={4} justify="flex-end" wrap="nowrap">
                    <Button
                      variant="light"
                      size="compact-sm"
                      leftSection={<IconShoppingCart size={14} />}
                      onClick={() => onDialog({ kind: 'purchase', ingredient })}
                    >
                      Закупка
                    </Button>
                    <Menu position="bottom-end" withinPortal>
                      <Menu.Target>
                        <ActionIcon variant="subtle" color="gray" aria-label={`Действия: ${ingredient.name}`}>
                          <IconDotsVertical size={16} />
                        </ActionIcon>
                      </Menu.Target>
                      <Menu.Dropdown>
                        <Menu.Item leftSection={<IconPencil size={16} />} onClick={() => onDialog({ kind: 'form', ingredient })}>
                          Изменить
                        </Menu.Item>
                        <Menu.Item
                          leftSection={<IconAdjustments size={16} />}
                          onClick={() => onDialog({ kind: 'adjust', ingredient })}
                        >
                          Инвентаризация
                        </Menu.Item>
                        <Menu.Item
                          leftSection={<IconTrashX size={16} />}
                          onClick={() => onDialog({ kind: 'writeOff', ingredient })}
                        >
                          Списать
                        </Menu.Item>
                        <Menu.Item leftSection={<IconHistory size={16} />} onClick={() => onHistory(ingredient)}>
                          История
                        </Menu.Item>
                        <Menu.Divider />
                        <Menu.Item
                          color="red"
                          leftSection={<IconTrash size={16} />}
                          onClick={() => removeIngredient(ingredient)}
                        >
                          Удалить
                        </Menu.Item>
                      </Menu.Dropdown>
                    </Menu>
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </div>
      {visible.length === 0 && (
        <Text size="sm" c="dimmed" ta="center" py="md">
          Ничего не найдено.
        </Text>
      )}
    </Section>
  );
}

function StockCell({ ingredient }: { ingredient: Ingredient }) {
  const text = `${quantity(ingredient.stockQuantity)} ${unitLabel[ingredient.unit]}`;
  if (ingredient.isOutOfStock) {
    return (
      <Badge color="red" variant="light" title="Со склада списано больше, чем было закуплено">
        {text}
      </Badge>
    );
  }
  if (ingredient.isLowStock) {
    return (
      <Badge
        color="orange"
        variant="light"
        title={`Меньше минимального остатка: ${quantity(ingredient.minStock)} ${unitLabel[ingredient.unit]}`}
      >
        {text}
      </Badge>
    );
  }
  return <>{text}</>;
}

// ---------- Карточка продукта ----------

function IngredientForm({ ingredient, onClose }: { ingredient: Ingredient | null; onClose: () => void }) {
  const currency = useCurrency();
  const create = useAction(api.ingredients.create, 'Продукт добавлен');
  const update = useAction(api.ingredients.update, 'Продукт сохранён');

  const [name, setName] = useState(ingredient?.name ?? '');
  const [kind, setKind] = useState<IngredientKind>(ingredient?.kind ?? 'Ingredient');
  const [unit, setUnit] = useState<MeasureUnit>(ingredient?.unit ?? 'Gram');
  const [packagePrice, setPackagePrice] = useState<number | string>(ingredient?.packagePrice ?? '');
  const [packageSize, setPackageSize] = useState<number | string>(ingredient?.packageSize ?? 1000);
  const [minStock, setMinStock] = useState<number | string>(ingredient?.minStock ?? '');
  const [initialStock, setInitialStock] = useState<number | string>('');

  const price = toNumber(packagePrice);
  const size = toNumber(packageSize);
  const canSave = name.trim() !== '' && price !== null && size !== null && size > 0;

  const changeKind = (value: IngredientKind) => {
    setKind(value);
    // Упаковку обычно считают штуками.
    if (!ingredient && value === 'Packaging') {
      setUnit('Piece');
      setPackageSize(1);
    }
  };

  const save = async () => {
    if (!canSave) return;
    const body = { name: name.trim(), kind, unit, packagePrice: price, packageSize: size, minStock: toNumber(minStock) };
    if (ingredient) await update.mutateAsync({ id: ingredient.id, ...body });
    else await create.mutateAsync({ ...body, initialStock: toNumber(initialStock) });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={ingredient ? 'Продукт' : 'Новый продукт'} centered>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void save().catch(() => undefined);
        }}
      >
        <Stack gap="sm">
          <SegmentedControl
            value={kind}
            onChange={(value) => changeKind(value as IngredientKind)}
            data={[
              { value: 'Ingredient', label: kindLabel.Ingredient },
              { value: 'Packaging', label: kindLabel.Packaging },
            ]}
          />
          <TextInput
            label="Название"
            placeholder={kind === 'Packaging' ? 'Например, Коробка для торта' : 'Например, Мука'}
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
            data-autofocus
            required
          />
          <Group gap="sm" align="flex-end" wrap="nowrap">
            <NumberField
              label={`Цена, ${currency}`}
              value={packagePrice}
              onChange={setPackagePrice}
              style={{ flex: 1 }}
              required
            />
            <NumberField
              label="за сколько"
              value={packageSize}
              onChange={setPackageSize}
              decimalScale={3}
              style={{ flex: 1 }}
              required
            />
            <Select
              aria-label="Единица измерения"
              data={[
                { value: 'Gram', label: 'г' },
                { value: 'Milliliter', label: 'мл' },
                { value: 'Piece', label: 'шт' },
              ]}
              value={unit}
              onChange={(value) => value && setUnit(value as MeasureUnit)}
              allowDeselect={false}
              w={90}
            />
          </Group>
          {canSave && (
            <Text size="xs" c="dimmed">
              {money(price)} {currency} за {quantity(size)} {unitLabel[unit]}
              {unit !== 'Piece' && size !== 1000 && ` — это ${money((price / size) * 1000)} за ${unit === 'Gram' ? 'кг' : 'л'}`}
            </Text>
          )}
          <Group gap="sm" grow>
            <NumberField
              label={`Минимальный остаток, ${unitLabel[unit]}`}
              description="Ниже него продукт подсветится"
              placeholder="Не следить"
              value={minStock}
              onChange={setMinStock}
              decimalScale={3}
            />
            {!ingredient && (
              <NumberField
                label={`Сейчас на складе, ${unitLabel[unit]}`}
                description="Если не знаете, оставьте пустым"
                value={initialStock}
                onChange={setInitialStock}
                decimalScale={3}
              />
            )}
          </Group>
          <Group justify="flex-end" gap="sm" mt="xs">
            <Button variant="default" onClick={onClose}>
              Отмена
            </Button>
            <Button type="submit" disabled={!canSave} loading={create.isPending || update.isPending}>
              Сохранить
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

// ---------- Закупка ----------

function PurchaseModal({ ingredient, onClose }: { ingredient: Ingredient; onClose: () => void }) {
  const currency = useCurrency();
  const purchase = useAction(api.ingredients.purchase, 'Закупка записана');
  const unit = unitLabel[ingredient.unit];

  const [date, setDate] = useState<string>(today());
  const [amount, setAmount] = useState<number | string>(ingredient.packageSize);
  const [totalCost, setTotalCost] = useState<number | string>(ingredient.packagePrice);
  const [updatePrice, setUpdatePrice] = useState(true);
  const [note, setNote] = useState('');

  const amountNumber = toNumber(amount);
  const costNumber = toNumber(totalCost);
  const canSave = amountNumber !== null && amountNumber > 0 && costNumber !== null;
  const newPackagePrice = canSave ? (costNumber / amountNumber) * ingredient.packageSize : null;
  const priceChanges = newPackagePrice !== null && Math.abs(newPackagePrice - ingredient.packagePrice) > 0.005;

  const save = async () => {
    if (!canSave) return;
    await purchase.mutateAsync({
      id: ingredient.id,
      date,
      quantity: amountNumber,
      totalCost: costNumber,
      updatePrice,
      note: note.trim() || null,
    });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={`Закупка: ${ingredient.name}`} centered>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void save().catch(() => undefined);
        }}
      >
        <Stack gap="sm">
          <Group gap="sm" grow align="flex-end">
            <NumberField label={`Купили, ${unit}`} value={amount} onChange={setAmount} decimalScale={3} data-autofocus required />
            <NumberField label={`Заплатили, ${currency}`} value={totalCost} onChange={setTotalCost} required />
          </Group>
          <DatePickerInput label="Дата" value={date} onChange={(value) => value && setDate(value)} valueFormat="D MMMM YYYY" />
          <Checkbox
            label="Обновить цену продукта по этой закупке"
            checked={updatePrice}
            onChange={(event) => setUpdatePrice(event.currentTarget.checked)}
          />
          {priceChanges && (
            <Text size="sm" c={updatePrice ? undefined : 'dimmed'}>
              {updatePrice ? 'Новая цена: ' : 'Цена по этой закупке: '}
              <b>
                {money(newPackagePrice)} за {quantity(ingredient.packageSize)} {unit}
              </b>{' '}
              (было {money(ingredient.packagePrice)}).{' '}
              {updatePrice && 'Себестоимость изделий пересчитается, уже записанные дни не изменятся.'}
            </Text>
          )}
          <TextInput
            label="Комментарий"
            placeholder="Необязательно: где купили"
            value={note}
            onChange={(event) => setNote(event.currentTarget.value)}
          />
          <Group justify="flex-end" gap="sm" mt="xs">
            <Button variant="default" onClick={onClose}>
              Отмена
            </Button>
            <Button type="submit" disabled={!canSave} loading={purchase.isPending}>
              Записать закупку
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

// ---------- Инвентаризация ----------

function AdjustModal({ ingredient, onClose }: { ingredient: Ingredient; onClose: () => void }) {
  const adjust = useAction(api.ingredients.adjust, 'Остаток исправлен');
  const unit = unitLabel[ingredient.unit];
  const [actual, setActual] = useState<number | string>(Math.max(ingredient.stockQuantity, 0));
  const [note, setNote] = useState('');

  const actualNumber = toNumber(actual);
  const difference = actualNumber === null ? 0 : actualNumber - ingredient.stockQuantity;
  const canSave = actualNumber !== null && difference !== 0;

  const save = async () => {
    if (!canSave) return;
    await adjust.mutateAsync({ id: ingredient.id, date: today(), actualQuantity: actualNumber, note: note.trim() || null });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={`Инвентаризация: ${ingredient.name}`} centered size="sm">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void save().catch(() => undefined);
        }}
      >
        <Stack gap="sm">
          <Text size="sm" c="dimmed">
            По программе на складе {quantity(ingredient.stockQuantity)} {unit}. Укажите, сколько есть на самом деле.
          </Text>
          <NumberField label={`Фактический остаток, ${unit}`} value={actual} onChange={setActual} decimalScale={3} data-autofocus />
          {canSave && (
            <Text size="sm">
              Разница: {difference > 0 ? '+' : '−'}
              {quantity(Math.abs(difference))} {unit}
            </Text>
          )}
          <TextInput label="Комментарий" placeholder="Необязательно" value={note} onChange={(event) => setNote(event.currentTarget.value)} />
          <Group justify="flex-end" gap="sm" mt="xs">
            <Button variant="default" onClick={onClose}>
              Отмена
            </Button>
            <Button type="submit" disabled={!canSave} loading={adjust.isPending}>
              Сохранить
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

// ---------- Списание сырья ----------

function StockWriteOffModal({ ingredient, onClose }: { ingredient: Ingredient; onClose: () => void }) {
  const writeOff = useAction(api.ingredients.writeOff, 'Списание записано');
  const unit = unitLabel[ingredient.unit];
  const [amount, setAmount] = useState<number | string>('');
  const [note, setNote] = useState('');

  const amountNumber = toNumber(amount);
  const canSave = amountNumber !== null && amountNumber > 0;

  const save = async () => {
    if (!canSave) return;
    await writeOff.mutateAsync({ id: ingredient.id, date: today(), quantity: amountNumber, note: note.trim() || null });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={`Списание: ${ingredient.name}`} centered size="sm">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void save().catch(() => undefined);
        }}
      >
        <Stack gap="sm">
          <Text size="sm" c="dimmed">
            Для испорченного или потерянного сырья. На складе {quantity(ingredient.stockQuantity)} {unit}.
          </Text>
          <NumberField label={`Списать, ${unit}`} value={amount} onChange={setAmount} decimalScale={3} data-autofocus />
          <TextInput label="Причина" placeholder="Например, истёк срок" value={note} onChange={(event) => setNote(event.currentTarget.value)} />
          <Group justify="flex-end" gap="sm" mt="xs">
            <Button variant="default" onClick={onClose}>
              Отмена
            </Button>
            <Button type="submit" color="red" disabled={!canSave} loading={writeOff.isPending}>
              Списать
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

// ---------- Журнал склада ----------

function Journal({
  ingredients,
  ingredientId,
  onIngredientChange,
}: {
  ingredients: Ingredient[];
  ingredientId: string | null;
  onIngredientChange: (id: string | null) => void;
}) {
  const currency = useCurrency();
  const confirm = useConfirm();
  const [range, setRange] = useState<[string | null, string | null]>([null, null]);
  const remove = useAction(api.stock.removeMovement, 'Запись удалена');

  const [from, to] = range;
  // Пока выбрана только первая дата периода, запрос не отправляется.
  const rangeReady = (from === null) === (to === null);
  const { data, error } = useStockMovements({ ingredientId, from, to }, rangeReady);

  const removeMovement = async (id: string, title: string) => {
    const ok = await confirm({
      title: 'Удалить запись?',
      message: `${title}. Остаток на складе вернётся к прежнему значению.`,
      confirmLabel: 'Удалить',
      danger: true,
    });
    if (ok) remove.mutate(id);
  };

  return (
    <Section>
      <Group gap="sm" mb="sm" wrap="wrap" justify="space-between">
        <Group gap="sm" wrap="wrap">
          <Select
            aria-label="Продукт"
            placeholder="Все продукты"
            data={ingredients.map((i) => ({ value: i.id, label: i.name }))}
            value={ingredientId}
            onChange={onIngredientChange}
            searchable
            clearable
            w={240}
          />
          <DatePickerInput
            type="range"
            aria-label="Период"
            placeholder="За всё время"
            value={range}
            onChange={setRange}
            valueFormat="D MMM YYYY"
            clearable
            w={260}
          />
        </Group>
        {data && data.purchasesTotal > 0 && (
          <Text size="sm" c="dimmed">
            Закуплено на {money(data.purchasesTotal)} {currency}
          </Text>
        )}
      </Group>

      {!data ? (
        <Loading error={error} />
      ) : data.items.length === 0 ? (
        <EmptyState title="Записей нет">
          Здесь появятся закупки, списание сырья на приготовленные изделия и инвентаризации.
        </EmptyState>
      ) : (
        <div className="table-scroll">
          <Table verticalSpacing="xs" horizontalSpacing="sm" highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Дата</Table.Th>
                <Table.Th>Продукт</Table.Th>
                <Table.Th>Операция</Table.Th>
                <Table.Th className="num">Количество</Table.Th>
                <Table.Th className="num">Сумма</Table.Th>
                <Table.Th>Комментарий</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.items.map((movement) => (
                <Table.Tr key={movement.id}>
                  <Table.Td>{shortDate(movement.date)}</Table.Td>
                  <Table.Td>{movement.ingredientName}</Table.Td>
                  <Table.Td>{movementLabel[movement.type]}</Table.Td>
                  <Table.Td className="num">
                    <Text span inherit c={movement.quantity > 0 ? 'teal.8' : undefined}>
                      {movement.quantity > 0 ? '+' : ''}
                      {quantity(movement.quantity)} {unitLabel[movement.unit]}
                    </Text>
                  </Table.Td>
                  <Table.Td className="num">{movement.totalCost === null ? '' : money(movement.totalCost)}</Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed">
                      {movement.note}
                    </Text>
                  </Table.Td>
                  <Table.Td w={40}>
                    {movement.canDelete && (
                      <ActionIcon
                        variant="subtle"
                        color="gray"
                        aria-label="Удалить запись"
                        onClick={() =>
                          removeMovement(
                            movement.id,
                            `${movementLabel[movement.type]}: ${movement.ingredientName}, ${quantity(Math.abs(movement.quantity))} ${unitLabel[movement.unit]}`,
                          )
                        }
                      >
                        <IconTrash size={16} />
                      </ActionIcon>
                    )}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </div>
      )}
    </Section>
  );
}
