import { Button, Code, CopyButton, Group, Stack, Text, TextInput } from '@mantine/core';
import { useEffect, useState } from 'react';
import { api } from '../api/client';
import { useAction, useSettings } from '../api/hooks';
import { NumberField } from '../components/inputs';
import { Loading, PageHeader, Section } from '../components/ui';
import { toNumber } from '../lib/format';

export function SettingsPage() {
  const { data: settings, error } = useSettings();
  const update = useAction(api.settings.update, 'Настройки сохранены');

  const [hourlyRate, setHourlyRate] = useState<number | string>('');
  const [currency, setCurrency] = useState('');

  useEffect(() => {
    if (settings) {
      setHourlyRate(settings.hourlyRate);
      setCurrency(settings.currency);
    }
  }, [settings]);

  if (!settings) {
    return (
      <Stack gap="md">
        <PageHeader title="Настройки" />
        <Loading error={error} />
      </Stack>
    );
  }

  const changed = (toNumber(hourlyRate) ?? 0) !== settings.hourlyRate || currency.trim() !== settings.currency;

  return (
    <Stack gap="md" maw={640}>
      <PageHeader title="Настройки" />

      <Section title="Расчёт себестоимости">
        <form
          onSubmit={(event) => {
            event.preventDefault();
            update.mutate({ hourlyRate: toNumber(hourlyRate) ?? 0, currency: currency.trim() });
          }}
        >
          <Stack gap="sm">
            <NumberField
              label="Стоимость часа работы"
              description="Если указать, время работы из карточки изделия войдёт в себестоимость. Ноль — работа не учитывается."
              value={hourlyRate}
              onChange={setHourlyRate}
              maw={260}
            />
            <TextInput
              label="Валюта"
              description="Подпись рядом с суммами"
              value={currency}
              onChange={(event) => setCurrency(event.currentTarget.value)}
              maw={260}
            />
            <Group>
              <Button type="submit" disabled={!changed || currency.trim() === ''} loading={update.isPending}>
                Сохранить
              </Button>
            </Group>
            <Text size="sm" c="dimmed">
              Новая ставка влияет на себестоимость изделий с этого момента. В уже записанных днях себестоимость не
              меняется; её можно пересчитать вручную в меню строки дня.
            </Text>
          </Stack>
        </form>
      </Section>

      <Section title="Где хранятся данные">
        <Stack gap="xs">
          <Text size="sm">Все данные лежат в одном файле на этом компьютере:</Text>
          <Group gap="sm" wrap="nowrap">
            <Code style={{ wordBreak: 'break-all' }}>{settings.databasePath}</Code>
            <CopyButton value={settings.databasePath}>
              {({ copied, copy }) => (
                <Button variant="light" size="compact-sm" onClick={copy}>
                  {copied ? 'Скопировано' : 'Копировать путь'}
                </Button>
              )}
            </CopyButton>
          </Group>
          <Text size="sm" c="dimmed">
            Чтобы сделать резервную копию, закройте программу и скопируйте этот файл, например на флешку. Чтобы
            восстановить данные, положите копию на то же место.
          </Text>
        </Stack>
      </Section>
    </Stack>
  );
}
