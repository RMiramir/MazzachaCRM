import { Alert, Button, Center, Group, Loader, Modal, Paper, Stack, Text, Title } from '@mantine/core';
import { IconAlertTriangle } from '@tabler/icons-react';
import { createContext, useCallback, useContext, useState, type ReactNode } from 'react';
import { money, signColor, signedMoney } from '../lib/format';

/** Заголовок страницы с действиями справа. */
export function PageHeader({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
      <Title order={2}>{title}</Title>
      {children && <Group gap="sm">{children}</Group>}
    </Group>
  );
}

/** Карточка с одной главной цифрой. */
export function StatCard({
  label,
  value,
  currency,
  hint,
  signed,
}: {
  label: string;
  value: number;
  currency: string;
  hint?: ReactNode;
  /** Показывать знак и красить по нему (для прибыли). */
  signed?: boolean;
}) {
  return (
    <Paper withBorder p="md" radius="md">
      <Text size="sm" c="dimmed">
        {label}
      </Text>
      <Group gap={6} align="baseline" wrap="nowrap">
        <Text fz={28} fw={700} lh={1.2} c={signed ? signColor(value) : undefined} className="tabular">
          {signed ? signedMoney(value) : money(value)}
        </Text>
        <Text size="sm" c="dimmed">
          {currency}
        </Text>
      </Group>
      {hint && (
        <Text size="xs" c="dimmed" mt={4}>
          {hint}
        </Text>
      )}
    </Paper>
  );
}

/** Сумма со знаком, окрашенная по нему. */
export function Profit({ value, fw }: { value: number | null | undefined; fw?: number }) {
  if (value === null || value === undefined) return <>—</>;
  return (
    <Text span inherit c={signColor(value)} fw={fw}>
      {signedMoney(value)}
    </Text>
  );
}

/** Блок с заголовком: таблица или форма внутри белой карточки. */
export function Section({ title, aside, children }: { title?: string; aside?: ReactNode; children: ReactNode }) {
  return (
    <Paper withBorder radius="md" p="md">
      {(title || aside) && (
        <Group justify="space-between" mb="sm" wrap="wrap" gap="xs">
          {title && <Title order={4}>{title}</Title>}
          {aside}
        </Group>
      )}
      {children}
    </Paper>
  );
}

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <Stack align="center" gap="xs" py="xl">
      <Text fw={600}>{title}</Text>
      {children && (
        <Text size="sm" c="dimmed" ta="center" maw={460}>
          {children}
        </Text>
      )}
    </Stack>
  );
}

/** Состояния загрузки и ошибки для данных страницы. */
export function Loading({ error }: { error?: Error | null }) {
  if (error) {
    return (
      <Alert color="red" icon={<IconAlertTriangle size={18} />} title="Не удалось загрузить данные">
        {error.message}
      </Alert>
    );
  }
  return (
    <Center py="xl">
      <Loader />
    </Center>
  );
}

// ---------- Подтверждение действий ----------

interface ConfirmOptions {
  title: string;
  message?: ReactNode;
  confirmLabel?: string;
  danger?: boolean;
}

const ConfirmContext = createContext<(options: ConfirmOptions) => Promise<boolean>>(async () => false);

export const useConfirm = () => useContext(ConfirmContext);

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<{ options: ConfirmOptions; resolve: (value: boolean) => void } | null>(null);

  const confirm = useCallback(
    (options: ConfirmOptions) => new Promise<boolean>((resolve) => setPending({ options, resolve })),
    [],
  );

  const close = (result: boolean) => {
    pending?.resolve(result);
    setPending(null);
  };

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      <Modal opened={pending !== null} onClose={() => close(false)} title={pending?.options.title} centered size="sm">
        {pending?.options.message && (
          <Text size="sm" mb="md">
            {pending.options.message}
          </Text>
        )}
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={() => close(false)}>
            Отмена
          </Button>
          <Button color={pending?.options.danger ? 'red' : undefined} onClick={() => close(true)} data-autofocus>
            {pending?.options.confirmLabel ?? 'Да'}
          </Button>
        </Group>
      </Modal>
    </ConfirmContext.Provider>
  );
}
