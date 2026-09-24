import type { ReactNode } from 'react';

/**
 * The monthly analysis's markdown (009), rendered as React elements.
 *
 * The content is the AI provider's text, and merchant and account names reach it from
 * user data, so it is untrusted. Nothing here builds HTML: every piece of the source
 * becomes a text child of an element chosen from a fixed list, and React escapes text.
 * A `<script>` in the content is shown as the characters `<script>`, never parsed.
 *
 * Only what the prompt asks the model to write is understood: `#` headings, paragraphs,
 * `-`/`*` and `1.` lists, `**bold**`, `*italic*` and `` `code` ``. Links, images, tables
 * and HTML are not (the prompt forbids them), so they stay as literal text and no URL
 * from the provider ever becomes an `href` or `src`.
 */
export default function Markdown({ source }: { source: string }) {
  return <div className="grid gap-3 text-sm leading-relaxed">{parseBlocks(source).map(renderBlock)}</div>;
}

type Block =
  | { kind: 'heading'; level: number; text: string }
  | { kind: 'paragraph'; text: string }
  | { kind: 'list'; ordered: boolean; items: string[] };

const HEADING = /^(#{1,6})\s+(.*?)\s*#*\s*$/;
const BULLET = /^\s*[-*+]\s+(.*)$/;
const NUMBERED = /^\s*\d+[.)]\s+(.*)$/;

function parseBlocks(source: string): Block[] {
  const blocks: Block[] = [];
  let paragraph: string[] = [];
  let list: { ordered: boolean; items: string[] } | null = null;

  const flush = () => {
    if (paragraph.length > 0) {
      blocks.push({ kind: 'paragraph', text: paragraph.join(' ') });
      paragraph = [];
    }

    if (list) {
      blocks.push({ kind: 'list', ...list });
      list = null;
    }
  };

  for (const raw of source.replace(/\r\n?/g, '\n').split('\n')) {
    const line = raw.trimEnd();
    const heading = HEADING.exec(line);
    const bullet = BULLET.exec(line);
    const numbered = bullet ? null : NUMBERED.exec(line);
    const item = bullet ?? numbered;

    if (line.trim() === '') {
      flush();
    } else if (heading) {
      flush();
      blocks.push({ kind: 'heading', level: heading[1]!.length, text: heading[2]! });
    } else if (item) {
      const ordered = numbered !== null;

      if (paragraph.length > 0 || (list && list.ordered !== ordered)) {
        flush();
      }

      list ??= { ordered, items: [] };
      list.items.push(item[1]!);
    } else if (list && /^\s/.test(raw)) {
      // An indented line continues the list item above it.
      list.items[list.items.length - 1] += ` ${line.trim()}`;
    } else {
      if (list) {
        flush();
      }

      paragraph.push(line.trim());
    }
  }

  flush();

  return blocks;
}

const HEADING_CLASSES = 'text-foreground mt-2 font-semibold first:mt-0';

function renderBlock(block: Block, index: number): ReactNode {
  switch (block.kind) {
    case 'heading': {
      // The card's own title is an h3, so the analysis's `##` sections sit under it.
      const level = Math.min(6, block.level + 2);
      const Tag = `h${level}` as 'h3' | 'h4' | 'h5' | 'h6';

      return (
        <Tag key={index} className={level <= 4 ? `${HEADING_CLASSES} text-base` : HEADING_CLASSES}>
          {renderInline(block.text)}
        </Tag>
      );
    }
    case 'paragraph':
      return <p key={index}>{renderInline(block.text)}</p>;
    case 'list': {
      const Tag = block.ordered ? 'ol' : 'ul';

      return (
        <Tag key={index} className={block.ordered ? 'list-decimal space-y-1 pl-5' : 'list-disc space-y-1 pl-5'}>
          {block.items.map((item, itemIndex) => (
            <li key={itemIndex}>{renderInline(item)}</li>
          ))}
        </Tag>
      );
    }
  }
}

/** `**bold**`, `` `code` `` and `*italic*`, the first that opens winning; the rest is text. */
const INLINE = /\*\*(.+?)\*\*|`([^`]+)`|\*(?!\s)(.+?)\*/;

function renderInline(text: string): ReactNode[] {
  const nodes: ReactNode[] = [];
  let rest = text;

  while (rest.length > 0) {
    const match = INLINE.exec(rest);

    if (!match) {
      nodes.push(rest);
      break;
    }

    if (match.index > 0) {
      nodes.push(rest.slice(0, match.index));
    }

    const key = nodes.length;
    const [, bold, code, italic] = match;

    if (bold !== undefined) {
      nodes.push(<strong key={key}>{renderInline(bold)}</strong>);
    } else if (code !== undefined) {
      nodes.push(
        <code key={key} className="bg-muted rounded px-1 py-0.5 text-xs">
          {code}
        </code>,
      );
    } else {
      nodes.push(<em key={key}>{renderInline(italic!)}</em>);
    }

    rest = rest.slice(match.index + match[0].length);
  }

  return nodes;
}
