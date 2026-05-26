export type RangePreset = 'thisMonth' | 'lastMonth' | 'ytd' | 'last12Months' | 'custom';

export interface DateRange {
  from: Date;
  to: Date;
}

export function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

export function endOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate(), 23, 59, 59, 999);
}

export function presetRange(preset: RangePreset, today: Date = new Date()): DateRange {
  const t = startOfDay(today);
  switch (preset) {
    case 'thisMonth':
      return {
        from: new Date(t.getFullYear(), t.getMonth(), 1),
        to: endOfDay(t),
      };
    case 'lastMonth': {
      const from = new Date(t.getFullYear(), t.getMonth() - 1, 1);
      const to = endOfDay(new Date(t.getFullYear(), t.getMonth(), 0));
      return { from, to };
    }
    case 'ytd':
      return {
        from: new Date(t.getFullYear(), 0, 1),
        to: endOfDay(t),
      };
    case 'last12Months': {
      const to = endOfDay(t);
      const from = new Date(t.getFullYear(), t.getMonth() - 11, 1);
      return { from, to };
    }
    case 'custom':
      return { from: t, to: endOfDay(t) };
  }
}

export function shiftRange(preset: RangePreset, current: DateRange, direction: -1 | 1): DateRange {
  switch (preset) {
    case 'thisMonth':
    case 'lastMonth': {
      const from = new Date(current.from.getFullYear(), current.from.getMonth() + direction, 1);
      const to = endOfDay(new Date(from.getFullYear(), from.getMonth() + 1, 0));
      return { from, to };
    }
    case 'ytd': {
      // Step a full calendar year. Anchor on the current `from` year.
      const targetYear = current.from.getFullYear() + direction;
      const from = new Date(targetYear, 0, 1);
      const to = endOfDay(new Date(targetYear, 11, 31));
      return { from, to };
    }
    case 'last12Months': {
      const from = new Date(current.from.getFullYear(), current.from.getMonth() + 12 * direction, 1);
      const lastMonth = new Date(from.getFullYear(), from.getMonth() + 12, 0);
      return { from, to: endOfDay(lastMonth) };
    }
    case 'custom': {
      const ms = current.to.getTime() - current.from.getTime() + 1;
      const from = new Date(current.from.getTime() + direction * ms);
      const to = new Date(current.to.getTime() + direction * ms);
      return { from: startOfDay(from), to: endOfDay(to) };
    }
  }
}

export function formatRangeLabel(preset: RangePreset, range: DateRange): string {
  const monthFmt = new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric' });
  const dateFmt = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' });
  switch (preset) {
    case 'thisMonth':
    case 'lastMonth':
      return monthFmt.format(range.from);
    case 'ytd':
      return `${range.from.getFullYear()}`;
    case 'last12Months':
      return `${monthFmt.format(range.from)} – ${monthFmt.format(range.to)}`;
    case 'custom':
      return `${dateFmt.format(range.from)} – ${dateFmt.format(range.to)}`;
  }
}
