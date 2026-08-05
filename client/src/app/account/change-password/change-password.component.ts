import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { AuthService } from '../../core/services/auth.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';

function passwordsMatch(control: AbstractControl): ValidationErrors | null {
  const newPassword = control.get('newPassword')?.value;
  const confirmPassword = control.get('confirmPassword')?.value;
  return newPassword && confirmPassword && newPassword !== confirmPassword ? { mismatch: true } : null;
}

/**
 * Client-side mirror of a rule the server also enforces (it answers 400 when the
 * new password equals the current one). Checking it here only buys a faster,
 * quieter message — the server response is still handled, because this control
 * pair can be bypassed and the server is the authority.
 */
function newPasswordDiffers(control: AbstractControl): ValidationErrors | null {
  const currentPassword = control.get('currentPassword')?.value;
  const newPassword = control.get('newPassword')?.value;
  return currentPassword && newPassword && currentPassword === newPassword ? { unchanged: true } : null;
}

/**
 * Voluntary self-service password change, reached from the topbar and guarded by
 * the `Account.ChangeOwnPassword` permission (seeded to SuperAdmin only). This is
 * the in-shell sibling of the forced screen in `auth/force-change-password` — it
 * hits the same `POST /auth/change-password` endpoint via the same
 * `AuthService.changePassword()`, and carries none of the forced flow's
 * "nothing else is reachable" framing.
 *
 * On success the session deliberately CONTINUES: the endpoint returns a fresh
 * `AuthResponse`, and `AuthService.changePassword()` already persists that token
 * pair, so the caller's access and refresh tokens are replaced in place. There is
 * therefore nothing to log out of and nowhere to navigate — the screen reports
 * success inline and clears the form. This is existing, server-proven behaviour,
 * not an assumption made by this component.
 *
 * Built from docs/DESIGN_TOKENS.md atoms only (ACTION_PLAN §7) — no new button,
 * card or form styling is introduced here.
 */
@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './change-password.component.html',
  styleUrl: './change-password.component.scss'
})
export class ChangePasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);

  readonly loading = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group(
    {
      currentPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', [Validators.required]]
    },
    { validators: [passwordsMatch, newPasswordDiffers] }
  );

  submit(): void {
    if (this.form.invalid || this.loading()) {
      this.form.markAllAsTouched();
      return;
    }
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.loading.set(true);

    const { currentPassword, newPassword } = this.form.getRawValue();
    this.auth.changePassword({ currentPassword, newPassword }).subscribe({
      next: () => {
        this.loading.set(false);
        this.successMessage.set('Password changed. Your next sign-in uses the new one.');
        this.form.reset();
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(
          extractErrorMessage(err, 'Could not change your password. Check your current password and try again.')
        );
      }
    });
  }
}
