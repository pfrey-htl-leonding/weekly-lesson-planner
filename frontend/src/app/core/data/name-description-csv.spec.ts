import {
  parseCourseScheduleCsv,
  parseNameDescriptionCsv,
  parseSchoolYearCsv,
  writeCourseScheduleCsv,
  writeNameDescriptionCsv,
  writeSchoolYearCsv,
} from './name-description-csv';

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

describe('school-year CSV', () => {
  it('round-trips the range and holiday/event markers', () => {
    const value = {
      name: '2026/27',
      planningStart: '2026-09-01',
      planningEnd: '2027-06-30',
      markers: [
        { date: '2026-10-26', type: 'Holiday' as const, label: 'National holiday' },
        { date: '2027-02-12', type: 'Event' as const, label: 'Open; day' },
      ],
    };

    expect(parseSchoolYearCsv(writeSchoolYearCsv(value))).toEqual(value);
  });

  it('rejects duplicate marker dates', () => {
    expect(() => parseSchoolYearCsv([
      'School year;Planning start;Planning end;Date;Type;Label',
      '2026/27;2026-09-01;2027-06-30;;;',
      '2026/27;2026-09-01;2027-06-30;2026-10-26;Holiday;',
      '2026/27;2026-09-01;2027-06-30;2026-10-26;Event;',
    ].join('\n'))).toThrowError('Line 4 repeats marker date 2026-10-26.');
  });
});

describe('course-schedule CSV', () => {
  it('round-trips course, date, and topic', () => {
    const records = [
      { course: '2AHIF', date: '2026-09-07', topic: 'SQL; joins', description: 'Inner and outer joins' },
      { course: '2AHIF', date: '2026-09-14', topic: 'Trees', description: '' },
    ];

    expect(parseCourseScheduleCsv(writeCourseScheduleCsv(records))).toEqual(records);
  });

  it('rejects duplicate course dates', () => {
    expect(() => parseCourseScheduleCsv([
      'Course;Date;Topic;Description',
      '2AHIF;2026-09-07;Trees;',
      '2AHIF;2026-09-07;Graphs;',
    ].join('\n'))).toThrowError('Line 3 repeats course date 2026-09-07.');
  });

  it('rejects calendar dates that do not exist', () => {
    expect(() => parseCourseScheduleCsv('Course;Date;Topic;Description\n2AHIF;2026-02-31;Trees;Search trees'))
      .toThrowError('Line 2 has an invalid date; use YYYY-MM-DD.');
  });
});
