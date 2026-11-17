import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';

import { GridFooter, PageChange } from './grid-footer';

@Component({
  imports: [GridFooter],
  template: `<app-grid-footer [total]="total()" [page]="page()" [pageSize]="pageSize()" (pageChange)="changes.push($event)" />`,
})
class Host {
  readonly total = signal(57);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly changes: PageChange[] = [];
}

describe('GridFooter', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;

  const root = () => fixture.nativeElement as HTMLElement;
  const button = (label: string) => root().querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Host],
      providers: [{ provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } }],
    }).compileComponents();
    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('says which rows are showing out of how many', () => {
    expect(root().textContent).toContain('1–20 of 57');
    expect(root().textContent).toContain('Page 1 of 3');

    host.page.set(3);
    fixture.detectChanges();

    expect(root().textContent).toContain('41–57 of 57'); // the last page is only partly full
  });

  it('says 0–0 of 0 when there is nothing to show', () => {
    host.total.set(0);
    fixture.detectChanges();

    expect(root().textContent).toContain('0–0 of 0');
    expect(root().textContent).toContain('Page 1 of 1');
  });

  it('cannot go back from the first page, or forward from the last', () => {
    expect(button('First page').disabled).toBe(true);
    expect(button('Previous page').disabled).toBe(true);
    expect(button('Next page').disabled).toBe(false);

    host.page.set(3);
    fixture.detectChanges();

    expect(button('Next page').disabled).toBe(true);
    expect(button('Last page').disabled).toBe(true);
    expect(button('Previous page').disabled).toBe(false);
  });

  it('reports the page the person asked for, keeping the page size', () => {
    button('Next page').click();
    button('Last page').click();

    expect(host.changes).toEqual([
      { page: 2, pageSize: 20 },
      { page: 3, pageSize: 20 },
    ]);
  });

  it('is its own bar: it holds the paging controls and nothing of the table', () => {
    expect(root().querySelector('nav[aria-label="Pages"]')).not.toBeNull();
    expect(root().querySelector('.ag-root')).toBeNull();
  });
});
