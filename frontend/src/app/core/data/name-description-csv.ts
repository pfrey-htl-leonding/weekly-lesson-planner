export interface NameDescriptionRecord {
  name: string;
  description: string;
}

export function writeNameDescriptionCsv(records: NameDescriptionRecord[]): string {
  return records
    .map(record => `${escapeField(record.name)};${escapeField(normalizeLineBreaks(record.description))}`)
    .join('\n');
}

export function parseNameDescriptionCsv(value: string): NameDescriptionRecord[] {
  return value
    .split(/\r?\n/)
    .map((line, index) => ({ line, number: index + 1 }))
    .filter(item => item.line.trim().length > 0)
    .map(item => {
      const fields = parseLine(item.line, item.number);
      if (fields.length > 2) {
        throw new Error(`Line ${item.number} contains more than two fields.`);
      }

      const name = fields[0]?.trim() ?? '';
      if (!name) {
        throw new Error(`Line ${item.number} has no name.`);
      }

      return { name, description: fields[1]?.trim() ?? '' };
    });
}

function escapeField(value: string): string {
  return /[;"\r\n]/.test(value) ? `"${value.replaceAll('"', '""')}"` : value;
}

function normalizeLineBreaks(value: string): string {
  return value.replace(/\s*[\r\n]+\s*/g, ' ');
}

function parseLine(line: string, lineNumber: number): string[] {
  const fields: string[] = [];
  let field = '';
  let quoted = false;

  for (let index = 0; index < line.length; index += 1) {
    const character = line[index];
    if (character === '"') {
      if (quoted && line[index + 1] === '"') {
        field += '"';
        index += 1;
      } else {
        quoted = !quoted;
      }
    } else if (character === ';' && !quoted) {
      fields.push(field);
      field = '';
    } else {
      field += character;
    }
  }

  if (quoted) {
    throw new Error(`Line ${lineNumber} contains an unclosed quote.`);
  }

  fields.push(field);
  return fields;
}
