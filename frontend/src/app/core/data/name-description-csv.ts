export interface NameDescriptionRecord {
  name: string;
  description: string;
}

export interface SchoolYearCsv {
  name: string;
  planningStart: string;
  planningEnd: string;
  markers: SchoolYearMarkerCsv[];
}

export interface SchoolYearMarkerCsv {
  date: string;
  type: 'Holiday' | 'Event';
  label: string;
}

export interface CourseScheduleCsvRecord {
  course: string;
  date: string;
  topic: string;
  description: string;
}

const schoolYearHeader = ['School year', 'Planning start', 'Planning end', 'Date', 'Type', 'Label'];
const courseScheduleHeader = ['Course', 'Date', 'Topic', 'Description'];

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

export function writeSchoolYearCsv(value: SchoolYearCsv): string {
  const common = [value.name, value.planningStart, value.planningEnd];
  return [
    writeRow(schoolYearHeader),
    writeRow([...common, '', '', '']),
    ...value.markers.map(marker => writeRow([...common, marker.date, marker.type, marker.label])),
  ].join('\n');
}

export function parseSchoolYearCsv(value: string): SchoolYearCsv {
  const rows = parseRows(value);
  requireHeader(rows, schoolYearHeader);
  if (rows.length < 2) throw new Error('The school-year CSV contains no data row.');

  const dataRows = rows.slice(1);
  const [name, planningStart, planningEnd] = dataRows[0].fields.map(field => field.trim());
  if (!name) throw new Error(`Line ${dataRows[0].number} has no school-year name.`);
  requireDate(planningStart, dataRows[0].number, 'planning start');
  requireDate(planningEnd, dataRows[0].number, 'planning end');
  if (planningEnd < planningStart) throw new Error('Planning end must be on or after planning start.');

  const markers: SchoolYearMarkerCsv[] = [];
  const dates = new Set<string>();
  for (const row of dataRows) {
    if (row.fields.length > schoolYearHeader.length) {
      throw new Error(`Line ${row.number} contains more than six fields.`);
    }
    const fields = [...row.fields, '', '', '', '', '', ''].slice(0, schoolYearHeader.length).map(field => field.trim());
    if (fields[0] !== name || fields[1] !== planningStart || fields[2] !== planningEnd) {
      throw new Error(`Line ${row.number} does not match the school-year name and range on line ${dataRows[0].number}.`);
    }
    if (!fields[3]) {
      if (fields[4] || fields[5]) throw new Error(`Line ${row.number} has marker data without a date.`);
      continue;
    }

    requireDate(fields[3], row.number, 'marker date');
    if (fields[3] < planningStart || fields[3] > planningEnd) {
      throw new Error(`Line ${row.number} has a marker outside the school-year range.`);
    }
    const type = normalizeMarkerType(fields[4], row.number);
    if (dates.has(fields[3])) throw new Error(`Line ${row.number} repeats marker date ${fields[3]}.`);
    dates.add(fields[3]);
    markers.push({ date: fields[3], type, label: fields[5] });
  }

  return { name, planningStart, planningEnd, markers };
}

export function writeCourseScheduleCsv(records: CourseScheduleCsvRecord[]): string {
  return [writeRow(courseScheduleHeader), ...records.map(record => writeRow([
    record.course,
    record.date,
    record.topic,
    record.description,
  ]))].join('\n');
}

export function parseCourseScheduleCsv(value: string): CourseScheduleCsvRecord[] {
  const rows = parseRows(value);
  requireHeader(rows, courseScheduleHeader);
  const dates = new Set<string>();
  return rows.slice(1).map(row => {
    if (row.fields.length > courseScheduleHeader.length) {
      throw new Error(`Line ${row.number} contains more than four fields.`);
    }
    const [course = '', date = '', topic = '', description = ''] = row.fields.map(field => field.trim());
    if (!course) throw new Error(`Line ${row.number} has no course.`);
    requireDate(date, row.number, 'date');
    if (!topic) throw new Error(`Line ${row.number} has no topic.`);
    if (dates.has(date)) throw new Error(`Line ${row.number} repeats course date ${date}.`);
    dates.add(date);
    return { course, date, topic, description };
  });
}

function escapeField(value: string): string {
  return /[;"\r\n]/.test(value) ? `"${value.replaceAll('"', '""')}"` : value;
}

function writeRow(fields: string[]): string {
  return fields.map(field => escapeField(normalizeLineBreaks(field))).join(';');
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

function parseRows(value: string): { fields: string[]; number: number }[] {
  return value
    .split(/\r?\n/)
    .map((line, index) => ({ line, number: index + 1 }))
    .filter(item => item.line.trim().length > 0)
    .map(item => ({ fields: parseLine(item.line, item.number), number: item.number }));
}

function requireHeader(rows: { fields: string[]; number: number }[], expected: string[]): void {
  if (rows.length === 0 || rows[0].fields.length !== expected.length ||
      rows[0].fields.some((field, index) => field.trim().toLocaleLowerCase() !== expected[index].toLocaleLowerCase())) {
    throw new Error(`Expected CSV header: ${expected.join(';')}`);
  }
}

function requireDate(value: string, lineNumber: number, field: string): void {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  const date = match ? new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]))) : null;
  if (!match || !date || date.getUTCFullYear() !== Number(match[1]) ||
      date.getUTCMonth() !== Number(match[2]) - 1 || date.getUTCDate() !== Number(match[3])) {
    throw new Error(`Line ${lineNumber} has an invalid ${field}; use YYYY-MM-DD.`);
  }
}

function normalizeMarkerType(value: string, lineNumber: number): 'Holiday' | 'Event' {
  if (value.toLocaleLowerCase() === 'holiday') return 'Holiday';
  if (value.toLocaleLowerCase() === 'event') return 'Event';
  throw new Error(`Line ${lineNumber} has an invalid marker type; use Holiday or Event.`);
}
