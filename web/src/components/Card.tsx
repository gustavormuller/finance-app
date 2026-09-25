import { cn } from '@/lib/utils';

/**
 * A glass panel (012): the unit every page is laid out in. A `<section>` by default,
 * so a card with a heading is a landmark a screen reader can jump to; pass `as="div"`
 * for a panel that is only visual.
 */
export default function Card({
  as: Tag = 'section',
  className,
  children,
  ...props
}: {
  as?: 'section' | 'div' | 'article';
  className?: string;
  children: React.ReactNode;
} & Omit<React.HTMLAttributes<HTMLElement>, 'className' | 'children'>): React.JSX.Element {
  return (
    <Tag className={cn('glass rounded-2xl p-5 sm:p-6', className)} {...props}>
      {children}
    </Tag>
  );
}
