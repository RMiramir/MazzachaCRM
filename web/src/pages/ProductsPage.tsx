import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Button,
  Divider,
  Grid,
  Group,
  Menu,
  Modal,
  Paper,
  SegmentedControl,
  Select,
  Stack,
  Switch,
  Table,
  Text,
  Textarea,
  TextInput,
  Tooltip,
} from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import {
  IconAlertTriangle,
  IconArchive,
  IconArchiveOff,
  IconDotsVertical,
  IconPencil,
  IconPlus,
  IconTrash,
} from '@tabler/icons-react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { useAction, useCurrency, useIngredients, useProduct, useProducts, useSettings } from '../api/hooks';
import type { CostMode, MeasureUnit, ProductDetail, ProductListItem, ProductType } from '../api/types';
import { NumberField } from '../components/inputs';
import { EmptyState, Loading, PageHeader, Profit, Section, useConfirm } from '../components/ui';
import { money, percent, quantity, toNumber, unitLabel } from '../lib/format';

export function ProductsPage() {
  const [showArchived, setShowArchived] = useState(false);
  const { data: products, error } = useProducts(showArchived);
  const { data: ingredients } = useIngredients();

  // null — окно закрыто, 'new' — новое изделие, иначе идентификатор изменяемого.
  const [editing, setEditing] = useState<string | null>(null);

  const finished = products?.filter((p) => p.type === 'Finished') ?? [];
  const semiFinished = products?.filter((p) => p.type === 'SemiFinished') ?? [];

  return (
    <Stack gap="md">
      <PageHeader title="Изделия">
        <Switch
          label="Показать архив"
          checked={showArchived}
          onChange={(event) => setShowArchived(event.currentTarget.checked)}
        />
        <Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>
          Новое изделие
        </Button>
      </PageHeader>

      {!products ? (
        <Loading error={error} />
      ) : products.length === 0 ? (
        <Section>
          <EmptyState title="Изделий пока нет">
            {ingredients && ingredients.length === 0 ? (
              <>
                Сначала заведите продукты с ценами на странице{' '}
                <Anchor component={Link} to="/ingredients">
                  «Продукты и склад»
                </Anchor>
                , затем добавьте изделие и укажите, сколько чего на него уходит.
              </>
            ) : (
              'Добавьте изделие и укажите его состав — программа посчитает себестоимость.'
            )}
          </EmptyState>
        </Section>
      ) : (
        <>
          <Section title="Готовые изделия">
            {finished.length === 0 ? (
              <Text size="sm" c="dimmed">
                Готовых изделий пока нет.
              </Text>
            ) : (
              <ProductsTable products={finished} onEdit={setEditing} />
            )}
          </Section>

          {semiFinished.length > 0 && (
            <Section
              title="Полуфабрикаты"
              aside={
                <Text size="sm" c="dimmed">
                  Заготовки, которые входят в состав других изделий
                </Text>
              }
            >
              <ProductsTable products={semiFinished} onEdit={setEditing} semiFinished />
            </Section>
          )}
        </>
      )}

      {editing !== null && <ProductEditor id={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </Stack>
  );
}

function ProductsTable({
  products,
  onEdit,
  semiFinished,
}: {
  products: ProductListItem[];
  onEdit: (id: string) => void;
  semiFinished?: boolean;
}) {
  const confirm = useConfirm();
  const archive = useAction(api.products.archive);
  const remove = useAction(api.products.remove, 'Изделие удалено');

  const removeProduct = async (product: ProductListItem) => {
    const ok = await confirm({
      title: 'Удалить изделие?',
      message: `«${product.name}» будет удалено вместе с составом.`,
      confirmLabel: 'Удалить',
      danger: true,
    });
    if (ok) remove.mutate(product.id);
  };

  return (
    <div className="table-scroll">
      <Table verticalSpacing="xs" horizontalSpacing="sm" highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Название</Table.Th>
            <Table.Th className="num">Выход партии</Table.Th>
            <Table.Th className="num">Себестоимость партии</Table.Th>
            <Table.Th className="num">{semiFinished ? 'С/с за единицу' : 'С/с за шт'}</Table.Th>
            {!semiFinished && (
              <>
                <Table.Th className="num">Цена</Table.Th>
                <Table.Th className="num">Прибыль за шт</Table.Th>
                <Table.Th className="num">Наценка</Table.Th>
              </>
            )}
            <Table.Th />
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {products.map((product) => (
            <Table.Tr key={product.id} style={{ cursor: 'pointer' }} onClick={() => onEdit(product.id)}>
              <Table.Td>
                <Group gap={8} wrap="nowrap">
                  <Text fw={500} size="sm" c={product.isArchived ? 'dimmed' : undefined}>
                    {product.name}
                  </Text>
                  {product.isArchived && (
                    <Badge size="xs" color="gray" variant="light">
                      В архиве
                    </Badge>
                  )}
                  {product.costMode === 'Manual' && (
                    <Badge size="xs" color="blue" variant="light">
                      С/с вручную
                    </Badge>
                  )}
                  {product.costError && (
                    <Tooltip label={product.costError}>
                      <IconAlertTriangle size={16} color="var(--mantine-color-red-6)" />
                    </Tooltip>
                  )}
                </Group>
              </Table.Td>
              <Table.Td className="num">
                {quantity(product.yieldQuantity)} {unitLabel[product.yieldUnit]}
              </Table.Td>
              <Table.Td className="num">{money(product.batchCost)}</Table.Td>
              <Table.Td className="num">
                {product.unitCost ? (
                  <Tooltip label={costParts(product.unitCost)}>
                    <span>{semiFinished ? unitCostText(product) : money(product.unitCost.total)}</span>
                  </Tooltip>
                ) : (
                  '—'
                )}
              </Table.Td>
              {!semiFinished && (
                <>
                  <Table.Td className="num">{money(product.salePrice)}</Table.Td>
                  <Table.Td className="num">
                    <Profit value={product.profitPerUnit} />
                  </Table.Td>
                  <Table.Td className="num">{percent(product.markupPercent)}</Table.Td>
                </>
              )}
              <Table.Td w={40} onClick={(event) => event.stopPropagation()}>
                <Menu position="bottom-end" withinPortal>
                  <Menu.Target>
                    <ActionIcon variant="subtle" color="gray" aria-label={`Действия: ${product.name}`}>
                      <IconDotsVertical size={16} />
                    </ActionIcon>
                  </Menu.Target>
                  <Menu.Dropdown>
                    <Menu.Item leftSection={<IconPencil size={16} />} onClick={() => onEdit(product.id)}>
                      Изменить
                    </Menu.Item>
                    {product.isArchived ? (
                      <Menu.Item
                        leftSection={<IconArchiveOff size={16} />}
                        onClick={() => archive.mutate({ id: product.id, archived: false })}
                      >
                        Вернуть из архива
                      </Menu.Item>
                    ) : (
                      <Menu.Item
                        leftSection={<IconArchive size={16} />}
                        onClick={() => archive.mutate({ id: product.id, archived: true })}
                      >
                        В архив
                      </Menu.Item>
                    )}
                    <Menu.Divider />
                    <Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => removeProduct(product)}>
                      Удалить
                    </Menu.Item>
                  </Menu.Dropdown>
                </Menu>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </div>
  );
}

function costParts(cost: { ingredients: number; packaging: number; labor: number }) {
  const parts = [`сырьё ${money(cost.ingredients)}`];
  if (cost.packaging > 0) parts.push(`упаковка ${money(cost.packaging)}`);
  if (cost.labor > 0) parts.push(`работа ${money(cost.labor)}`);
  return parts.join(' · ');
}

/** Полуфабрикат в граммах стоит доли за грамм, поэтому показываем цену за килограмм или литр. */
function unitCostText(product: ProductListItem) {
  const cost = product.unitCost!.total;
  if (product.yieldUnit === 'Gram') return `${money(cost * 1000)} за кг`;
  if (product.yieldUnit === 'Milliliter') return `${money(cost * 1000)} за л`;
  return `${money(cost)} за шт`;
}

// ---------- Редактор изделия ----------

function ProductEditor({ id, onClose }: { id: string | null; onClose: () => void }) {
  const { data: detail, error } = useProduct(id);

  return (
    <Modal opened onClose={onClose} title={id === null ? 'Новое изделие' : 'Изделие'} size="xl" centered>
      {id !== null && !detail ? <Loading error={error} /> : <ProductForm initial={detail ?? null} onClose={onClose} />}
    </Modal>
  );
}

interface DraftLine {
  key: number;
  /** «i:идентификатор» для продукта, «p:идентификатор» для изделия. */
  component: string | null;
  quantity: number | string;
}

let nextLineKey = 1;
const emptyLine = (): DraftLine => ({ key: nextLineKey++, component: null, quantity: '' });

function ProductForm({ initial, onClose }: { initial: ProductDetail | null; onClose: () => void }) {
  const currency = useCurrency();
  const { data: settings } = useSettings();
  const { data: ingredients } = useIngredients();
  const { data: products } = useProducts(true);

  const [name, setName] = useState(initial?.name ?? '');
  const [type, setType] = useState<ProductType>(initial?.type ?? 'Finished');
  const [yieldQuantity, setYieldQuantity] = useState<number | string>(initial?.yieldQuantity ?? 1);
  const [yieldUnit, setYieldUnit] = useState<MeasureUnit>(initial?.yieldUnit ?? 'Piece');
  const [laborMinutes, setLaborMinutes] = useState<number | string>(initial?.laborMinutes || '');
  const [costMode, setCostMode] = useState<CostMode>(initial?.costMode ?? 'Recipe');
  const [manualUnitCost, setManualUnitCost] = useState<number | string>(initial?.manualUnitCost || '');
  const [salePrice, setSalePrice] = useState<number | string>(initial?.salePrice || '');
  const [note, setNote] = useState(initial?.note ?? '');
  const [lines, setLines] = useState<DraftLine[]>(
    initial
      ? initial.lines.map((line) => ({
          key: nextLineKey++,
          component: line.ingredientId ? `i:${line.ingredientId}` : `p:${line.componentProductId}`,
          quantity: line.quantity,
        }))
      : [emptyLine()],
  );

  const create = useAction(api.products.create, 'Изделие добавлено');
  const update = useAction(api.products.update, 'Изделие сохранено');

  const isFinished = type === 'Finished';
  const isManual = costMode === 'Manual';

  // Что можно добавить в состав: продукты, упаковка и другие изделия (кроме самого себя).
  const componentOptions = useMemo(() => {
    const byName = (a: { label: string }, b: { label: string }) => a.label.localeCompare(b.label, 'ru');
    const ofKind = (kind: string) =>
      (ingredients ?? [])
        .filter((i) => i.kind === kind)
        .map((i) => ({ value: `i:${i.id}`, label: i.name }))
        .sort(byName);
    const others = (products ?? [])
      .filter((p) => p.id !== initial?.id && !p.isArchived)
      .map((p) => ({ value: `p:${p.id}`, label: p.name }))
      .sort(byName);

    return [
      { group: 'Продукты', items: ofKind('Ingredient') },
      { group: 'Упаковка', items: ofKind('Packaging') },
      { group: 'Изделия и полуфабрикаты', items: others },
    ].filter((g) => g.items.length > 0);
  }, [ingredients, products, initial?.id]);

  const unitOf = (component: string | null): string => {
    if (!component) return '';
    const [kind, id] = [component[0], component.slice(2)];
    if (kind === 'i') {
      const ingredient = ingredients?.find((i) => i.id === id);
      return ingredient ? unitLabel[ingredient.unit] : '';
    }
    const product = products?.find((p) => p.id === id);
    return product ? unitLabel[product.yieldUnit] : '';
  };

  const lineInputs = lines.map((line) => ({
    ingredientId: line.component?.startsWith('i:') ? line.component.slice(2) : null,
    componentProductId: line.component?.startsWith('p:') ? line.component.slice(2) : null,
    quantity: toNumber(line.quantity) ?? 0,
  }));

  // Живой пересчёт себестоимости, пока состав редактируется.
  const draft = {
    productId: initial?.id ?? null,
    yieldQuantity: toNumber(yieldQuantity) ?? 0,
    laborMinutes: toNumber(laborMinutes) ?? 0,
    salePrice: isFinished ? (toNumber(salePrice) ?? 0) : 0,
    lines: lineInputs,
    costMode,
    manualUnitCost: toNumber(manualUnitCost) ?? 0,
  };
  const [debouncedDraft] = useDebouncedValue(draft, 250);
  const preview = useQuery({
    queryKey: ['cost-preview', debouncedDraft],
    queryFn: () => api.products.previewCost(debouncedDraft),
    placeholderData: keepPreviousData,
    retry: false,
  });

  const completeLines = lineInputs.filter((l) => (l.ingredientId || l.componentProductId) && l.quantity > 0);
  const canSave =
    name.trim() !== '' && draft.yieldQuantity > 0 && (isManual ? toNumber(manualUnitCost) !== null : completeLines.length > 0);

  const save = async () => {
    const body = {
      name: name.trim(),
      type,
      yieldQuantity: draft.yieldQuantity,
      yieldUnit: isFinished ? ('Piece' as const) : yieldUnit,
      laborMinutes: draft.laborMinutes,
      salePrice: draft.salePrice,
      note: note.trim() || null,
      lines: isManual ? [] : completeLines,
      costMode,
      manualUnitCost: draft.manualUnitCost,
    };
    if (initial) await update.mutateAsync({ id: initial.id, ...body });
    else await create.mutateAsync(body);
    onClose();
  };

  const changeLine = (key: number, patch: Partial<DraftLine>) =>
    setLines(lines.map((line) => (line.key === key ? { ...line, ...patch } : line)));

  const cost = preview.data;
  const yieldUnitText = isFinished ? 'шт' : unitLabel[yieldUnit];
  const noIngredients = ingredients !== undefined && ingredients.length === 0;

  return (
    <Grid gutter="lg">
      <Grid.Col span={{ base: 12, md: 8 }}>
        <Stack gap="sm">
          <SegmentedControl
            value={type}
            onChange={(value) => setType(value as ProductType)}
            data={[
              { value: 'Finished', label: 'Готовое изделие' },
              { value: 'SemiFinished', label: 'Полуфабрикат' },
            ]}
          />
          <TextInput
            label="Название"
            placeholder={isFinished ? 'Например, Медовый торт (кусок)' : 'Например, Крем сливочный'}
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
            data-autofocus
            required
          />

          <Group gap="sm" align="flex-end" grow>
            <NumberField
              label="Выход партии"
              description={isFinished ? 'Сколько штук получается' : 'Сколько получается из состава'}
              value={yieldQuantity}
              onChange={setYieldQuantity}
              decimalScale={3}
              required
            />
            {isFinished ? (
              <NumberField
                label={`Цена за шт, ${currency}`}
                description="Подставляется в таблицу дня"
                value={salePrice}
                onChange={setSalePrice}
              />
            ) : (
              <Select
                label="Единица выхода"
                description="В чём измеряется заготовка"
                data={[
                  { value: 'Gram', label: 'граммы' },
                  { value: 'Milliliter', label: 'миллилитры' },
                  { value: 'Piece', label: 'штуки' },
                ]}
                value={yieldUnit}
                onChange={(value) => value && setYieldUnit(value as MeasureUnit)}
                allowDeselect={false}
              />
            )}
            {!isManual && (
              <NumberField
                label="Работа, минут"
                description="На всю партию"
                value={laborMinutes}
                onChange={setLaborMinutes}
                decimalScale={0}
              />
            )}
          </Group>

          <Group gap="sm" mt="xs" align="center">
            <Text size="sm" fw={500}>
              Себестоимость:
            </Text>
            <SegmentedControl
              size="xs"
              value={costMode}
              onChange={(value) => setCostMode(value as CostMode)}
              data={[
                { value: 'Recipe', label: 'По составу' },
                { value: 'Manual', label: 'Указать вручную' },
              ]}
            />
          </Group>

          {isManual ? (
            <Stack gap={4}>
              <NumberField
                label={`Себестоимость за 1 ${isFinished ? 'шт' : unitLabel[yieldUnit]}, ${currency}`}
                description="Когда цена известна без расчёта: покупной товар или изделие, состав которого вы ещё не заводили. Сырьё со склада не списывается."
                value={manualUnitCost}
                onChange={setManualUnitCost}
                decimalScale={4}
                maw={360}
              />
            </Stack>
          ) : (
          <>
          <Divider label="Состав на одну партию" labelPosition="left" mt="xs" />

          {noIngredients && (
            <Alert color="orange" variant="light">
              Продуктов ещё нет. Сначала добавьте их на странице{' '}
              <Anchor component={Link} to="/ingredients">
                «Продукты и склад»
              </Anchor>
              .
            </Alert>
          )}

          <Stack gap={6}>
            {lines.map((line, index) => (
              <Group key={line.key} gap="xs" wrap="nowrap" align="center">
                <Select
                  aria-label="Компонент"
                  placeholder="Продукт, упаковка или изделие"
                  data={componentOptions}
                  value={line.component}
                  onChange={(value) => changeLine(line.key, { component: value })}
                  searchable
                  nothingFoundMessage="Ничего не найдено"
                  style={{ flex: 1 }}
                />
                <NumberField
                  aria-label="Количество"
                  placeholder="Сколько"
                  value={line.quantity}
                  onChange={(value) => changeLine(line.key, { quantity: value })}
                  decimalScale={3}
                  w={110}
                  rightSection={
                    <Text size="xs" c="dimmed">
                      {unitOf(line.component)}
                    </Text>
                  }
                />
                <Text size="sm" w={70} ta="right" className="tabular" c="dimmed">
                  {money(cost?.lineCosts[index] ?? 0)}
                </Text>
                <ActionIcon
                  variant="subtle"
                  color="gray"
                  onClick={() => setLines(lines.length === 1 ? [emptyLine()] : lines.filter((l) => l.key !== line.key))}
                  aria-label="Убрать из состава"
                >
                  <IconTrash size={16} />
                </ActionIcon>
              </Group>
            ))}
          </Stack>
          <Group>
            <Button
              variant="light"
              size="compact-sm"
              leftSection={<IconPlus size={14} />}
              onClick={() => setLines([...lines, emptyLine()])}
            >
              Добавить в состав
            </Button>
          </Group>
          </>
          )}

          <Textarea
            label="Заметка"
            placeholder="Необязательно"
            value={note}
            onChange={(event) => setNote(event.currentTarget.value)}
            autosize
            minRows={1}
          />
        </Stack>
      </Grid.Col>

      <Grid.Col span={{ base: 12, md: 4 }}>
        <Paper withBorder p="md" radius="md" bg="gray.0">
          <Text fw={600} mb="xs">
            Себестоимость
          </Text>
          {preview.error ? (
            <Alert color="red" variant="light" icon={<IconAlertTriangle size={16} />}>
              {preview.error.message}
            </Alert>
          ) : (
            <Stack gap={6}>
              <CostRow label="Сырьё" value={cost?.batchCost.ingredients} />
              <CostRow label="Упаковка" value={cost?.batchCost.packaging} />
              <CostRow label="Работа" value={cost?.batchCost.labor} />
              <Divider />
              <CostRow label="Вся партия" value={cost?.batchCost.total} strong />
              <CostRow
                label={`За 1 ${yieldUnitText}`}
                value={cost?.unitCost.total}
                strong
                precise={!isFinished && yieldUnit !== 'Piece'}
              />

              {isFinished && draft.salePrice > 0 && cost && (
                <>
                  <Divider />
                  <Group justify="space-between">
                    <Text size="sm">Прибыль за шт</Text>
                    <Text size="sm" fw={600}>
                      <Profit value={cost.profitPerUnit} />
                    </Text>
                  </Group>
                  <Group justify="space-between">
                    <Text size="sm">Наценка</Text>
                    <Text size="sm" fw={600}>
                      {percent(cost.markupPercent)}
                    </Text>
                  </Group>
                  {cost.breakEvenUnits !== null && (
                    <Text size="xs" c="dimmed">
                      Партия окупится, если продать {cost.breakEvenUnits} из {quantity(draft.yieldQuantity)} шт
                    </Text>
                  )}
                </>
              )}

              {draft.laborMinutes > 0 && settings?.hourlyRate === 0 && (
                <Text size="xs" c="orange.8">
                  Ставка за час не задана, поэтому работа не входит в себестоимость. Её можно указать в{' '}
                  <Anchor component={Link} to="/settings" size="xs">
                    настройках
                  </Anchor>
                  .
                </Text>
              )}
            </Stack>
          )}
        </Paper>

        <Group justify="flex-end" gap="sm" mt="md">
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
      </Grid.Col>
    </Grid>
  );
}

function CostRow({
  label,
  value,
  strong,
  precise,
}: {
  label: string;
  value: number | undefined;
  strong?: boolean;
  /** Цена за грамм или миллилитр: нужно больше знаков. */
  precise?: boolean;
}) {
  const text =
    value === undefined
      ? '—'
      : precise
        ? new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 4 }).format(value)
        : money(value);
  return (
    <Group justify="space-between">
      <Text size="sm" fw={strong ? 600 : undefined}>
        {label}
      </Text>
      <Text size="sm" fw={strong ? 700 : undefined} className="tabular">
        {text}
      </Text>
    </Group>
  );
}
