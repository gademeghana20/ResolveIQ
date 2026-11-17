import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { API_URL } from '../../../core/api-config';
import { Category } from '../../../core/category.models';
import { CategoryDialog, CategoryDialogData } from './category-dialog';

const hardware: Category = { id: 'c-1', name: 'Hardware', description: 'Laptops and printers', isActive: true };
const hiddenOne: Category = { id: 'c-2', name: 'Old stuff', description: null, isActive: false };

describe('CategoryDialog', () => {
  let fixture: ComponentFixture<CategoryDialog>;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const nameBox = () => page().querySelector('input[formControlName="name"]') as HTMLInputElement;
  const descriptionBox = () => page().querySelector('textarea') as HTMLTextAreaElement;
  const submitButton = () => page().querySelector('button[type="submit"]') as HTMLButtonElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp(data: CategoryDialogData = {}) {
    close = vi.fn();
    await TestBed.configureTestingModule({
      imports: [CategoryDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CategoryDialog);
    await settle();
  }

  function type(box: HTMLInputElement | HTMLTextAreaElement, value: string) {
    box.value = value;
    box.dispatchEvent(new Event('input'));
  }

  async function submit() {
    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  describe('creating', () => {
    beforeEach(() => setUp());

    it('is titled "New category", and starts empty', () => {
      expect(page().textContent).toContain('New category');
      expect(nameBox().value).toBe('');
    });

    it('needs a name', async () => {
      await submit();

      http.expectNone(`${API_URL}/categories`);
      expect(page().textContent).toContain('A name is required.');
    });

    it('does not accept a name that is only spaces', async () => {
      type(nameBox(), '    ');
      await submit();

      http.expectNone(`${API_URL}/categories`);
      expect(page().textContent).toContain('A name is required.');
    });

    it('warns when the name is longer than 100 characters', async () => {
      type(nameBox(), 'x'.repeat(101));
      await submit();

      http.expectNone(`${API_URL}/categories`);
      expect(page().textContent).toContain('limit is 100');
    });

    it('warns when the description is longer than 500 characters', async () => {
      type(nameBox(), 'Network');
      type(descriptionBox(), 'x'.repeat(501));
      await submit();

      http.expectNone(`${API_URL}/categories`);
      expect(page().textContent).toContain('limit is 500');
    });

    it('counts characters as you type', async () => {
      type(nameBox(), 'Network');
      await settle();

      expect(page().textContent).toContain('7 / 100');
    });

    it('sends the trimmed name, an empty description as nothing, and closes with the saved category', async () => {
      type(nameBox(), '  Network  ');
      await submit();

      const request = http.expectOne(`${API_URL}/categories`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ name: 'Network', description: null });
      request.flush({ id: 'c-9', name: 'Network', description: null, isActive: true });

      expect(close).toHaveBeenCalledWith({ id: 'c-9', name: 'Network', description: null, isActive: true });
    });

    it('sends the description when there is one', async () => {
      type(nameBox(), 'Network');
      type(descriptionBox(), '  Wi-Fi and VPN  ');
      await submit();

      expect(http.expectOne(`${API_URL}/categories`).request.body).toEqual({ name: 'Network', description: 'Wi-Fi and VPN' });
    });

    it('cannot be submitted twice while saving', async () => {
      type(nameBox(), 'Network');
      await submit();

      expect(submitButton().disabled).toBe(true);
      expect(submitButton().textContent).toContain('Saving');
      http.expectOne(`${API_URL}/categories`).flush(hardware);
    });

    it('puts "already exists" next to the name, stays open, and keeps what was typed', async () => {
      type(nameBox(), 'hardware');
      await submit();

      http.expectOne(`${API_URL}/categories`).flush(
        { errors: { Name: ['A category with this name already exists.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();

      expect(page().textContent).toContain('A category with this name already exists.');
      expect(nameBox().value).toBe('hardware');
      expect(close).not.toHaveBeenCalled();
      expect(submitButton().disabled).toBe(false);
    });

    it('says so when the server cannot be reached', async () => {
      type(nameBox(), 'Network');
      await submit();

      http.expectOne(`${API_URL}/categories`).error(new ProgressEvent('error'));
      await settle();

      expect(page().textContent).toContain('Cannot reach the server');
      expect(close).not.toHaveBeenCalled();
    });
  });

  describe('editing', () => {
    it('is titled "Edit category" and starts with the current name and description', async () => {
      await setUp({ category: hardware });

      expect(page().textContent).toContain('Edit category');
      expect(nameBox().value).toBe('Hardware');
      expect(descriptionBox().value).toBe('Laptops and printers');
    });

    it('saves changes to that category', async () => {
      await setUp({ category: hardware });
      type(nameBox(), 'Hardware & devices');

      await submit();

      const request = http.expectOne(`${API_URL}/categories/c-1`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ name: 'Hardware & devices', description: 'Laptops and printers', isActive: true });
      request.flush({ ...hardware, name: 'Hardware & devices' });

      expect(close).toHaveBeenCalledWith({ ...hardware, name: 'Hardware & devices' });
    });

    it('does not accidentally show a hidden category again when it is renamed', async () => {
      await setUp({ category: hiddenOne });
      type(nameBox(), 'Archived');

      await submit();

      expect(http.expectOne(`${API_URL}/categories/c-2`).request.body).toMatchObject({ isActive: false });
    });

    it('can clear the description', async () => {
      await setUp({ category: hardware });
      type(descriptionBox(), '');

      await submit();

      expect(http.expectOne(`${API_URL}/categories/c-1`).request.body).toMatchObject({ description: null });
    });

    it('says so when the category has since been removed', async () => {
      await setUp({ category: hardware });
      await submit();

      http.expectOne(`${API_URL}/categories/c-1`).flush({}, { status: 404, statusText: 'Not Found' });
      await settle();

      expect(page().textContent).toContain('no longer exists');
    });
  });
});
