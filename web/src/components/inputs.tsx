import { NumberInput, type NumberInputProps } from '@mantine/core';
import { useEffect, useState } from 'react';
import { toNumber } from '../lib/format';

/** Поле для чисел: запятая как разделитель, без стрелок, без отрицательных значений. */
export function NumberField(props: NumberInputProps) {
  return (
    <NumberInput
      decimalSeparator=","
      allowedDecimalSeparators={[',', '.']}
      thousandSeparator=" "
      allowNegative={false}
      hideControls
      decimalScale={2}
      {...props}
    />
  );
}

interface InlineNumberProps {
  value: number;
  /** Сохраняет новое значение. Если сохранение не удалось, поле возвращает прежнее число. */
  onCommit: (value: number) => Promise<unknown>;
  label: string;
  disabled?: boolean;
  width?: number;
  decimalScale?: number;
}

/**
 * Число прямо в ячейке таблицы. Сохраняется по Enter или при уходе с поля, Esc отменяет ввод.
 */
export function InlineNumber({ value, onCommit, label, disabled, width = 64, decimalScale = 2 }: InlineNumberProps) {
  const [draft, setDraft] = useState<number | string>(value);
  const [saving, setSaving] = useState(false);

  useEffect(() => setDraft(value), [value]);

  const commit = async () => {
    const next = toNumber(draft) ?? 0;
    if (next === value) {
      setDraft(value);
      return;
    }
    setSaving(true);
    try {
      await onCommit(next);
    } catch {
      setDraft(value);
    } finally {
      setSaving(false);
    }
  };

  return (
    <NumberField
      aria-label={label}
      size="xs"
      w={width}
      value={draft}
      onChange={setDraft}
      onBlur={commit}
      onFocus={(event) => event.currentTarget.select()}
      onKeyDown={(event) => {
        if (event.key === 'Enter') event.currentTarget.blur();
        if (event.key === 'Escape') {
          setDraft(value);
          event.currentTarget.blur();
        }
      }}
      disabled={disabled || saving}
      decimalScale={decimalScale}
      styles={{ input: { textAlign: 'right', fontVariantNumeric: 'tabular-nums' } }}
    />
  );
}
