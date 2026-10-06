import { describe, expect, it } from 'vitest';

import { ApiError } from '@/api/finance';

import { refusal, refusalMessage } from './refusal';

const badRequest = new ApiError(400, 'One or more validation errors occurred.', {
  name: ['O nome é obrigatório.'],
  parentId: ['Categoria não encontrada.'],
});

describe('refusal', () => {
  it('puts the shown fields under the form and the others above it', () => {
    expect(refusal(badRequest, ['name'])).toEqual({ fields: badRequest.fields, message: 'Categoria não encontrada.' });
  });

  it('leaves no sentence when every field is shown', () => {
    expect(refusal(badRequest, ['name', 'parentId']).message).toBeNull();
  });

  it('uses the problem detail when no field is named', () => {
    expect(refusal(new ApiError(409, 'Já existe uma conta chamada “Nubank”.'), ['name'])).toEqual({
      fields: {},
      message: 'Já existe uma conta chamada “Nubank”.',
    });
  });
});

describe('refusalMessage', () => {
  it('joins every field message when none is on screen', () => {
    expect(refusalMessage(badRequest)).toBe('O nome é obrigatório. Categoria não encontrada.');
  });

  it('is the error message for anything else, and a sentence for a non-error', () => {
    expect(refusalMessage(new Error('Failed to fetch'))).toBe('Failed to fetch');
    expect(refusalMessage('boom')).toBe('Algo deu errado.');
  });
});
