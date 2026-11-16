import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterOutlet } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';

/** The frame around every signed-in page: the top bar, and a spot where the current page appears. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, MatToolbarModule, MatButtonModule],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
})
export class Shell {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected signOut(): void {
    this.auth.logout();
    this.router.navigateByUrl('/login');
  }
}
