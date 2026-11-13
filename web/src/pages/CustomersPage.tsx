import { ActionIcon, Badge, Button, Checkbox, Group, Menu, Modal, Stack, Table, Text, Textarea, TextInput } from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { IconDotsVertical, IconPencil, IconPlus, IconSearch, IconTrash } from '@tabler/icons-react';
import { useState } from 'react';
import { api } from '../api/client';
import { useAction, useCustomers, useOrders } from '../api/hooks';
import type { Customer } from '../api/types';
import { ShopBadge } from '../components/orders';
import { EmptyState, Loading, PageHeader, Section, useConfirm } from '../components/ui';
import { money, orderStatusColor, orderStatusLabel, shortDate } from '../lib/format';

export function CustomersPage() {
  const [search, setSearch] = useState('');
  const [debouncedSearch] = useDebouncedValue(search, 250);
  const { data: customers, error } = useCustomers(debouncedSearch);
  const confirm = useConfirm();
  const remove = useAction(api.customers.remove, 'Клиент удалён');

  // null — окно закрыто, 'new' — новый клиент, иначе изменяемый клиент.
  const [editing, setEditing] = useState<Customer | 'new' | null>(null);

  const removeCustomer = async (customer: Customer) => {
    const ok = await confirm({
      title: 'Удалить клиента?',
      message: `«${customer.name}» будет удалён из списка.`,
      confirmLabel: 'Удалить',
      danger: true,
    });
    if (ok) remove.mutate(customer.id);
  };

  const nothingYet = customers?.length === 0 && debouncedSearch.trim() === '';

  return (
    <Stack gap="md">
      <PageHeader title="Клиенты">
        <TextInput
          aria-label="Поиск"
          placeholder="Имя или телефон"
          leftSection={<IconSearch size={16} />}
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          w={220}
        />
        <Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>
          Новый клиент
        </Button>
      </PageHeader>

      {!customers ? (
        <Loading error={error} />
      ) : nothingYet ? (
        <Section>
          <EmptyState title="Клиентов пока нет">
            Клиент появляется здесь, когда вы записываете для него заказ. Можно добавить и заранее.
          </EmptyState>
        </Section>
      ) : (
        <Section>
          <div className="table-scroll">
            <Table verticalSpacing="xs" horizontalSpacing="sm" highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Имя</Table.Th>
                  <Table.Th>Телефон</Table.Th>
                  <Table.Th>Заметка</Table.Th>
                  <Table.Th className="num">Заказов</Table.Th>
                  <Table.Th className="num">На сумму</Table.Th>
                  <Table.Th className="num">Должен</Table.Th>
                  <Table.Th>Последний заказ</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {customers.map((customer) => (
                  <Table.Tr key={customer.id} style={{ cursor: 'pointer' }} onClick={() => setEditing(customer)}>
                    <Table.Td>
                      <Group gap={6} wrap="nowrap">
                        <Text size="sm" fw={500}>
                          {customer.name}
                        </Text>
                        {customer.isConsignment && <ShopBadge />}
                      </Group>
                    </Table.Td>
                    <Table.Td>{customer.phone}</Table.Td>
                    <Table.Td>
                      <Text size="sm" c="dimmed" lineClamp={1}>
                        {customer.note}
                      </Text>
                    </Table.Td>
                    <Table.Td className="num">{customer.ordersCount}</Table.Td>
                    <Table.Td className="num">{money(customer.ordersTotal)}</Table.Td>
                    <Table.Td className="num">
                      {customer.debt > 0 ? (
                        <Text span inherit fw={600} c="orange.8">
                          {money(customer.debt)}
                        </Text>
                      ) : (
                        ''
                      )}
                    </Table.Td>
                    <Table.Td>{shortDate(customer.lastOrderDate)}</Table.Td>
                    <Table.Td w={40} onClick={(event) => event.stopPropagation()}>
                      <Menu position="bottom-end" withinPortal>
                        <Menu.Target>
                          <ActionIcon variant="subtle" color="gray" aria-label={`Действия: ${customer.name}`}>
                            <IconDotsVertical size={16} />
                          </ActionIcon>
                        </Menu.Target>
                        <Menu.Dropdown>
                          <Menu.Item leftSection={<IconPencil size={16} />} onClick={() => setEditing(customer)}>
                            Изменить
                          </Menu.Item>
                          <Menu.Item color="red" leftSection={<IconTrash size={16} />} onClick={() => removeCustomer(customer)}>
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
          {customers.length === 0 && (
            <Text size="sm" c="dimmed" ta="center" py="md">
              Никого не нашли.
            </Text>
          )}
        </Section>
      )}

      {editing !== null && <CustomerForm customer={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </Stack>
  );
}

function CustomerForm({ customer, onClose }: { customer: Customer | null; onClose: () => void }) {
  const create = useAction(api.customers.create, 'Клиент добавлен');
  const update = useAction(api.customers.update, 'Клиент сохранён');
  const { data: orders } = useOrders({ customerId: customer?.id }, customer !== null);

  const [name, setName] = useState(customer?.name ?? '');
  const [phone, setPhone] = useState(customer?.phone ?? '');
  const [note, setNote] = useState(customer?.note ?? '');
  const [isConsignment, setIsConsignment] = useState(customer?.isConsignment ?? false);

  const save = async () => {
    const body = { name: name.trim(), phone: phone.trim() || null, note: note.trim() || null, isConsignment };
    if (customer) await update.mutateAsync({ id: customer.id, ...body });
    else await create.mutateAsync(body);
    onClose();
  };

  return (
    <Modal opened onClose={onClose} title={customer ? 'Клиент' : 'Новый клиент'} centered>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void save().catch(() => undefined);
        }}
      >
        <Stack gap="sm">
          <TextInput label="Имя" value={name} onChange={(event) => setName(event.currentTarget.value)} data-autofocus required />
          <TextInput label="Телефон" value={phone} onChange={(event) => setPhone(event.currentTarget.value)} />
          <Textarea
            label="Заметка"
            placeholder="Что любит, аллергии, адрес"
            value={note}
            onChange={(event) => setNote(event.currentTarget.value)}
            autosize
            minRows={2}
          />
          <Checkbox
            label="Магазин: платит после продажи, может вернуть непроданное"
            checked={isConsignment}
            onChange={(event) => setIsConsignment(event.currentTarget.checked)}
          />

          {customer && orders && orders.length > 0 && (
            <div>
              <Text fw={600} size="sm" mb={4}>
                Заказы
              </Text>
              <Stack gap={4}>
                {orders.map((order) => (
                  <Group key={order.id} justify="space-between" wrap="nowrap" gap="sm">
                    <Text size="sm" lineClamp={1}>
                      №{order.number} · {shortDate(order.dueDate)} · {order.itemsSummary}
                    </Text>
                    <Group gap={8} wrap="nowrap">
                      <Text size="sm" className="tabular">
                        {money(order.total)}
                      </Text>
                      <Badge size="sm" color={orderStatusColor[order.status]} variant="light">
                        {orderStatusLabel[order.status]}
                      </Badge>
                    </Group>
                  </Group>
                ))}
              </Stack>
            </div>
          )}

          <Group justify="flex-end" gap="sm" mt="xs">
            <Button variant="default" onClick={onClose}>
              Отмена
            </Button>
            <Button type="submit" disabled={name.trim() === ''} loading={create.isPending || update.isPending}>
              Сохранить
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
