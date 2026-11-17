import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { Observable } from 'rxjs';

import { firstValidationMessage } from '../../../core/api-error';
import { Role } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { UserSummary } from '../../../core/user.models';
import { Badge } from '../../../shared/badge/badge';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { AdminService } from '../admin.service';
import { ChangeRoleDialog, ChangeRoleDialogData } from './change-role-dialog';
import { CreateUserDialog } from './create-user-dialog';
import { ResetPasswordDialog, ResetPasswordDialogData } from './reset-password-dialog';

type LoadState = 'loading' | 'ready' | 'failed';

@Component({
  selector: 'app-users',
  imports: [
    ReactiveFormsModule,
    MatTableModule,
    MatButtonModule,
    MatMenuModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    Badge,
  ],
  templateUrl: './users.html',
  styleUrl: './users.css',
})
export class Users {
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = ['email', 'role', 'status', 'actions'];

  protected readonly state = signal<LoadState>('loading');
  protected readonly users = signal<UserSummary[]>([]);
  protected readonly busy = signal(false);

  protected readonly search = new FormControl('', { nonNullable: true });
  private readonly searchText = toSignal(this.search.valueChanges, { initialValue: '' });

  /** Who is looking at the screen. The API does not let you change or deactivate yourself. */
  protected readonly me = this.auth.userId;

  protected readonly visible = computed(() => {
    const text = this.searchText().trim().toLowerCase();

    return [...this.users()]
      .filter((user) => !text || (user.email ?? '').toLowerCase().includes(text))
      .sort((a, b) => (a.email ?? '').localeCompare(b.email ?? '', undefined, { sensitivity: 'base' }));
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set('loading');

    this.admin
      .getUsers()
      // load() also runs later (Try again, or after a refused change), so the cleanup handle is passed in.
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (users) => {
          this.users.set(users);
          this.state.set('ready');
        },
        error: () => this.state.set('failed'),
      });
  }

  protected roleOf(user: UserSummary): string {
    return user.roles.join(', ') || 'No role';
  }

  protected isMe(user: UserSummary): boolean {
    return user.id === this.me();
  }

  protected createUser(): void {
    this.dialog
      .open<CreateUserDialog, void, UserSummary>(CreateUserDialog)
      .afterClosed()
      .subscribe((created) => {
        if (created) {
          this.users.update((list) => [...list, created]);
          this.tell(`${created.email} was created as ${this.roleOf(created)}.`);
        }
      });
  }

  protected changeRole(user: UserSummary): void {
    const data: ChangeRoleDialogData = { email: user.email ?? 'this person', current: (user.roles[0] as Role) ?? null };

    this.dialog
      .open<ChangeRoleDialog, ChangeRoleDialogData, Role>(ChangeRoleDialog, { data })
      .afterClosed()
      .subscribe((role) => {
        if (role) {
          this.run(this.admin.changeRole(user.id, role), `${user.email} is now ${role}. They were signed out.`);
        }
      });
  }

  protected resetPassword(user: UserSummary): void {
    const data: ResetPasswordDialogData = { userId: user.id, email: user.email ?? 'this person' };

    this.dialog
      .open<ResetPasswordDialog, ResetPasswordDialogData, boolean>(ResetPasswordDialog, { data })
      .afterClosed()
      .subscribe((saved) => {
        if (saved) {
          this.tell(`The password for ${user.email} was changed. Tell them the new one.`);
        }
      });
  }

  protected toggleActive(user: UserSummary): void {
    if (!user.isActive) {
      // Bringing someone back is harmless, so no question is asked.
      this.run(this.admin.setActive(user.id, true), `${user.email} can sign in again.`);
      return;
    }

    this.dialog
      .open(ConfirmDialog, {
        data: {
          title: `Deactivate ${user.email}?`,
          message:
            'They are signed out at once and cannot sign in until you reactivate them. Their incidents and history stay exactly as they are.',
          confirmLabel: 'Deactivate',
        },
      })
      .afterClosed()
      .subscribe((confirmed) => {
        if (confirmed) {
          this.run(this.admin.setActive(user.id, false), `${user.email} was deactivated.`);
        }
      });
  }

  /** Sends one change, then swaps the updated person into the table. */
  private run(request: Observable<UserSummary>, successMessage: string): void {
    this.busy.set(true);

    request.subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.users.update((list) => list.map((user) => (user.id === updated.id ? updated : user)));
        this.tell(successMessage);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.tell(firstValidationMessage(error) ?? 'That could not be done. Please try again.');
        this.load(); // what is on screen may be out of date
      },
    });
  }

  private tell(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: 8000 });
  }
}
