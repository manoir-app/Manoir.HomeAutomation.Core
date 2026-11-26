import type * as React from 'react';

const stroke = {
  fill: 'none',
  stroke: 'currentColor',
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  strokeWidth: 1.6,
};

const glyphs = {
  grid: <><rect height="5.5" rx="1.6" width="5.5" x="3" y="3" /><rect height="5.5" rx="1.6" width="5.5" x="11.5" y="3" /><rect height="5.5" rx="1.6" width="5.5" x="3" y="11.5" /><rect height="5.5" rx="1.6" width="5.5" x="11.5" y="11.5" /></>,
  sliders: <path d="M3 6h8M15 6h2M3 14h2M9 14h8M11 4v4M7 12v4" />,
  server: <><rect height="5" rx="1.6" width="14" x="3" y="3.5" /><rect height="5" rx="1.6" width="14" x="3" y="11.5" /><path d="M6 6h.01M6 14h.01" /></>,
} satisfies Record<string, React.ReactNode>;

export type IconKind = keyof typeof glyphs;

export function Icon({ kind }: { kind: IconKind }) {
  return (
    <svg aria-hidden="true" height="20" viewBox="0 0 20 20" width="20" {...stroke}>
      {glyphs[kind]}
    </svg>
  );
}

export function SunIcon() {
  return (
    <svg aria-hidden="true" height="20" viewBox="0 0 20 20" width="20" {...stroke}>
      <circle cx="10" cy="10" r="3.2" />
      <path d="M10 2.5v1.8M10 15.7v1.8M2.5 10h1.8M15.7 10h1.8M4.7 4.7l1.3 1.3M14 14l1.3 1.3M4.7 15.3 6 14M14 6l1.3-1.3" />
    </svg>
  );
}

export function MoonIcon() {
  return (
    <svg aria-hidden="true" height="20" viewBox="0 0 20 20" width="20" {...stroke}>
      <path d="M16.5 11.6A6.8 6.8 0 0 1 8.4 3.5a6.8 6.8 0 1 0 8.1 8.1Z" />
    </svg>
  );
}
