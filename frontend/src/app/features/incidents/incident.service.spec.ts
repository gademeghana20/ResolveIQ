import { IncidentQuery } from './incident.models';
import { toParams } from './incident.service';

const base: IncidentQuery = { sortBy: 'CreatedAt', sortDirection: 'Desc', page: 1, pageSize: 20 };

describe('toParams', () => {
  it('always sends paging and sorting', () => {
    const params = toParams(base);

    expect(params.get('page')).toBe('1');
    expect(params.get('pageSize')).toBe('20');
    expect(params.get('sortBy')).toBe('CreatedAt');
    expect(params.get('sortDirection')).toBe('Desc');
  });

  it('leaves out every filter that is not set', () => {
    expect(toParams(base).keys().sort()).toEqual(['page', 'pageSize', 'sortBy', 'sortDirection']);
  });

  it('sends the filters that are set', () => {
    const params = toParams({
      ...base,
      search: 'printer',
      status: 'Open',
      priority: 'High',
      categoryId: 'abc',
      slaState: 'Breached',
      assignedToMe: true,
    });

    expect(params.get('search')).toBe('printer');
    expect(params.get('status')).toBe('Open');
    expect(params.get('priority')).toBe('High');
    expect(params.get('categoryId')).toBe('abc');
    expect(params.get('slaState')).toBe('Breached');
    expect(params.get('assignedToMe')).toBe('true');
    expect(params.has('reportedByMe')).toBe(false);
  });

  it('ignores a search that is only spaces, and trims the rest', () => {
    expect(toParams({ ...base, search: '   ' }).has('search')).toBe(false);
    expect(toParams({ ...base, search: '  vpn  ' }).get('search')).toBe('vpn');
  });
});
