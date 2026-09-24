import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import Markdown from './Markdown';

describe('Markdown', () => {
  /** Spec web unit test 27, first half: the provider's markdown renders. */
  it('renders headings, paragraphs, lists, bold, italic and inline code', () => {
    const { container } = render(
      <Markdown
        source={[
          '## Resumo',
          '',
          'Você gastou **R$ 3.200,50** em *setembro*, com `12` lançamentos.',
          'A linha seguinte continua o parágrafo.',
          '',
          '- Alimentação: R$ 2.000,00',
          '* Moradia: R$ 1.200,00',
          '',
          '1. Revise as assinaturas',
          '2. Compare o mercado',
        ].join('\n')}
      />,
    );

    expect(screen.getByRole('heading', { name: 'Resumo' })).toBeInTheDocument();
    expect(container.querySelector('strong')).toHaveTextContent('R$ 3.200,50');
    expect(container.querySelector('em')).toHaveTextContent('setembro');
    expect(container.querySelector('code')).toHaveTextContent('12');
    expect(container.querySelector('p')).toHaveTextContent(
      'Você gastou R$ 3.200,50 em setembro, com 12 lançamentos. A linha seguinte continua o parágrafo.',
    );

    const [bullets, numbered] = screen.getAllByRole('list');
    expect(bullets?.tagName).toBe('UL');
    expect(bullets?.querySelectorAll('li')).toHaveLength(2);
    expect(numbered?.tagName).toBe('OL');
    expect(Array.from(numbered?.querySelectorAll('li') ?? []).map((item) => item.textContent)).toEqual([
      'Revise as assinaturas',
      'Compare o mercado',
    ]);
  });

  /**
   * Spec web unit test 27, second half: markdown from the provider is untrusted, and a
   * merchant or account name can carry markup into it. Every tag is text on screen.
   */
  it('escapes script tags and any other HTML in the content', () => {
    const { container } = render(
      <Markdown
        source={[
          '## Resumo <script>alert("h1")</script>',
          '',
          'Gasto em <script>alert("xss")</script> e <img src=x onerror="alert(1)">.',
          '',
          '- **<b>negrito</b>** e [link](javascript:alert(1))',
          '',
          '<iframe src="https://example.com"></iframe>',
        ].join('\n')}
      />,
    );

    expect(container.querySelector('script')).toBeNull();
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('iframe')).toBeNull();
    expect(container.querySelector('b')).toBeNull();
    expect(container.querySelector('a')).toBeNull();

    expect(screen.getByText(/Gasto em <script>alert\("xss"\)<\/script> e <img src=x onerror="alert\(1\)">\./)).toBeInTheDocument();
    expect(screen.getByRole('heading')).toHaveTextContent('Resumo <script>alert("h1")</script>');
    expect(container.querySelector('strong')).toHaveTextContent('<b>negrito</b>');
    expect(screen.getByText(/<iframe src="https:\/\/example.com"><\/iframe>/)).toBeInTheDocument();
  });
});
