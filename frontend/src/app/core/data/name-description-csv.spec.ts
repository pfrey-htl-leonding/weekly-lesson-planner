import { parseNameDescriptionCsv, writeNameDescriptionCsv } from './name-description-csv';

describe('name/description CSV', () => {
  it('parses names with optional descriptions', () => {
    expect(parseNameDescriptionCsv('Arrays\nTrees;Binary search trees')).toEqual([
      { name: 'Arrays', description: '' },
      { name: 'Trees', description: 'Binary search trees' },
    ]);
  });

  it('round-trips semicolons and quotes', () => {
    const records = [{ name: 'SQL; joins', description: 'Use "outer" joins' }];

    expect(parseNameDescriptionCsv(writeNameDescriptionCsv(records))).toEqual(records);
  });

  it('rejects more than two fields', () => {
    expect(() => parseNameDescriptionCsv('Name;Description;Unexpected')).toThrowError(
      'Line 1 contains more than two fields.',
    );
  });
});
