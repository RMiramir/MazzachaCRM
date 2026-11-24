import { ActionIcon, Autocomplete, Badge, Button, Checkbox, Group, Modal, Select, Stack, Text, TextInput } from '@mantine/core';
import { DatePickerInput } from '@mantine/dates';
import { notifications } from '@mantine/notifications';
import { IconPlus, IconTrash } from '@tabler/icons-react';
import { useState } from 'react';
import { api } from '../api/client';
import { useAction, useCurrency } from '../api/hooks';
import type { Ingredient, IngredientKind, MeasureUnit, PurchaseLineInput } from '../api/types';
import { money, quantity, toNumber, today, unitLabel } from '../lib/format';
import { NumberField } from './inputs';

interface Line {
  key: number;
  name: string;
  /** Единица и тип нужны только для нового продукта. */
  unit: MeasureUnit;
  kind: IngredientKind;
  quantity: number | string;
  totalCost: number | string;
  updatePrice: boolean;
}

let nextKey = 1;
const emptyLine = (): Line => ({
  key: nextKey++,
  name: '',
  unit: 'Gram',
  kind: 'Ingredient',
  quantity: '',
  totalCost: '',
  updatePrice: true,
});

const sameName = (a: string, b: string) => a.trim().toLocaleLowerCase('ru') === b.trim().toLocaleLowerCase('ru');

/** Привычная упаковка для цены нового продукта: килограмм, литр или штука. */
const defaultPackage = (unit: MeasureUnit) => (unit === 'Piece' ? 1 : 1000);

/**
 * Общая закупка: несколько продуктов за один раз. Название подсказывается из списка;
 * если такого продукта ещё нет, он создаётся вместе с закупкой.
 */
export function BulkPurchaseModal({ ingredients, onClose }: { ingredients: Ingredient[]; onClose: () => void }) {
  const currency = useCurrency();
  const purchase = useAction(api.stock.purchases);
  const [date, setDate] = useState<string>(today());
  const [note, setNote] = useState('');
  const [lines, setLines] = useState<Line[]>([emptyLine()]);

  const find = (name: string) => ingredients.find((i) => sameName(i.name, name));

  const change = (key: number, patch: Partial<Line>) => {
    const next = lines.map((line) => (line.key === key ? { ...line, ...patch } : line));
    // Как только в последней строке появилось название, снизу добавляется пустая.
    const last = next[next.length - 1];
    setLines(last.name.trim() !== '' ? [...next, emptyLine()] : next);
  };

  const filled = lines.filter((line) => line.name.trim() !== '' || toNumber(line.quantity) !== null || toNumber(line.totalCost) !== null);
  const isValid = (line: Line) =>
    line.name.trim() !== '' && (toNumber(line.quantity) ?? 0) > 0 && toNumber(line.totalCost) !== null;
  const canSave = filled.length > 0 && filled.every(isValid);
  const total = filled.reduce((sum, line) => sum + (toNumber(line.totalCost) ?? 0), 0);

  const save = async () => {
    const body: PurchaseLineInput[] = filled.map((line) => {
      const existing = find(line.name);
      return {
        ingredientId: existing?.id ?? null,
        newName: existing ? null : line.name.trim(),
        newKind: existing ? null : line.kind,
        newUnit: existing ? null : line.unit,
        quantity: toNumber(line.quantity)!,
        totalCost: toNumber(line.totalCost)!,
        updatePrice: line.updatePrice,
      };
    });
    const result = await purchase.mutateAsync({ date, lines: body, note: note.trim() || null });
    notifications.show({ message: `Закупка записана: позиций — ${result.count}`, color: 'teal', autoClose: 3000 });
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title="Закупка" size="xl" centered>
      <Stack gap="sm">
        <Text size="sm" c="dimmed">
          Перечислите всё, что купили. Начните вводить название: существующий продукт подставится, новое название
          создаст продукт. Если цена изменилась, она обновится сразу.
        </Text>

        <Stack gap="xs">
          {lines.map((line, index) => {
            const existing = find(line.name);
            const isNew = line.name.trim() !== '' && !existing;
            const unit = existing?.unit ?? line.unit;
            const amount = toNumber(line.quantity);
            const cost = toNumber(line.totalCost);
            const ready = amount !== null && amount > 0 && cost !== null;

            const packageSize = existing?.packageSize ?? defaultPackage(unit);
            const newPrice = ready ? (cost / amount) * packageSize : null;
            const priceChanges = existing && newPrice !== null && Math.abs(newPrice - existing.packagePrice) > 0.005;

            return (
              <div key={line.key}>
                <Group gap="xs" wrap="nowrap" align="flex-end">
                  <Autocomplete
                    label={index === 0 ? 'Продукт' : undefined}
                    aria-label="Продукт"
                    placeholder="Название, например Мука"
                    data={ingredients.map((i) => i.name)}
                    value={line.name}
                    onChange={(value) => change(line.key, { name: value })}
                    style={{ flex: 1, minWidth: 180 }}
                    data-autofocus={index === 0 || undefined}
                  />
                  {isNew && (
                    <>
                      <Select
                        label={index === 0 ? 'Единица' : undefined}
                        aria-label="Единица нового продукта"
                        data={[
                          { value: 'Gram', label: 'г' },
                          { value: 'Milliliter', label: 'мл' },
                          { value: 'Piece', label: 'шт' },
                        ]}
                        value={line.unit}
                        onChange={(value) => value && change(line.key, { unit: value as MeasureUnit })}
                        allowDeselect={false}
                        w={80}
                      />
                      <Select
                        label={index === 0 ? 'Тип' : undefined}
                        aria-label="Тип нового продукта"
                        data={[
                          { value: 'Ingredient', label: 'Продукт' },
                          { value: 'Packaging', label: 'Упаковка' },
                        ]}
                        value={line.kind}
                        onChange={(value) => value && change(line.key, { kind: value as IngredientKind })}
                        allowDeselect={false}
                        w={125}
                      />
                    </>
                  )}
                  <NumberField
                    label={index === 0 ? 'Купили' : undefined}
                    aria-label="Сколько купили"
                    value={line.quantity}
                    onChange={(value) => change(line.key, { quantity: value })}
                    decimalScale={3}
                    w={130}
                    rightSection={
                      <Text size="xs" c="dimmed">
                        {unitLabel[unit]}
                      </Text>
                    }
                  />
                  <NumberField
                    label={index === 0 ? `Заплатили, ${currency}` : undefined}
                    aria-label="Сколько заплатили"
                    value={line.totalCost}
                    onChange={(value) => change(line.key, { totalCost: value })}
                    w={150}
                  />
                  <ActionIcon
                    variant="subtle"
                    color="gray"
                    size="lg"
                    onClick={() => setLines(lines.length === 1 ? [emptyLine()] : lines.filter((l) => l.key !== line.key))}
                    aria-label="Убрать строку"
                  >
                    <IconTrash size={16} />
                  </ActionIcon>
                </Group>

                {isNew && (
                  <Group gap={8} mt={4}>
                    <Badge size="md" color="blue" variant="light">
                      Новый продукт
                    </Badge>
                    {newPrice !== null && (
                      <Text size="xs" c="dimmed">
                        Цена: {money(newPrice)} за {quantity(packageSize)} {unitLabel[unit]}
                      </Text>
                    )}
                  </Group>
                )}
                {existing && (
                  <Group gap="md" mt={4} wrap="wrap">
                    {priceChanges ? (
                      <>
                        <Text size="xs" c={line.updatePrice ? 'orange.8' : 'dimmed'}>
                          Цена за {quantity(packageSize)} {unitLabel[unit]}: было {money(existing.packagePrice)} →{' '}
                          {line.updatePrice ? 'станет' : 'по этой закупке'} <b>{money(newPrice)}</b>
                        </Text>
                        <Checkbox
                          size="xs"
                          label="обновить цену"
                          checked={line.updatePrice}
                          onChange={(event) => change(line.key, { updatePrice: event.currentTarget.checked })}
                        />
                      </>
                    ) : (
                      <Text size="xs" c="dimmed">
                        Цена {money(existing.packagePrice)} за {quantity(packageSize)} {unitLabel[unit]}
                        {ready ? ' — не меняется' : ''}
                      </Text>
                    )}
                    <Text size="xs" c="dimmed">
                      На складе {quantity(existing.stockQuantity)} {unitLabel[unit]}
                      {ready && ` → ${quantity(existing.stockQuantity + amount)}`}
                    </Text>
                  </Group>
                )}
              </div>
            );
          })}
        </Stack>

        <Group>
          <Button variant="light" size="compact-sm" leftSection={<IconPlus size={14} />} onClick={() => setLines([...lines, emptyLine()])}>
            Ещё строка
          </Button>
        </Group>

        <Group gap="sm" align="flex-end" grow>
          <DatePickerInput label="Дата закупки" value={date} onChange={(value) => value && setDate(value)} valueFormat="D MMMM YYYY" />
          <TextInput label="Комментарий" placeholder="Необязательно: где купили" value={note} onChange={(event) => setNote(event.currentTarget.value)} />
        </Group>

        <Group justify="space-between" mt="xs">
          <Text>
            Итого: <b>{money(total)}</b> {currency}
          </Text>
          <Group gap="sm">
            <Button variant="default" onClick={onClose}>
              Отмена
            </Button>
            <Button onClick={() => void save().catch(() => undefined)} disabled={!canSave} loading={purchase.isPending}>
              Записать закупку
            </Button>
          </Group>
        </Group>
      </Stack>
    </Modal>
  );
}
