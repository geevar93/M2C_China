import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';

function passwordsMatch(control: AbstractControl): ValidationErrors | null {
  const newPassword = control.get('newPassword')?.value;
  const confirmPassword = control.get('confirmPassword')?.value;
  return newPassword && confirmPassword && newPassword !== confirmPassword ? { mismatch: true } : null;
}

/**
 * Force-password-change screen — no prototype precedent (TECH_SPEC OI-4/
 * ACTION_PLAN E0-03). Built from docs/DESIGN_TOKENS.md atoms only. No shell
 * navigation is reachable from here (this component is rendered outside the
 * app shell, and the shell's own route is blocked by mustChangePasswordGuard
 * until this succeeds). Still needs business sign-off (E0-06).
 */
@Component({
  selector: 'app-force-change-password',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './force-change-password.component.html',
  styleUrl: './force-change-password.component.scss'
})
export class ForceChangePasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly loading = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group(
    {
      currentPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', [Validators.required]]
    },
    { validators: passwordsMatch }
  );

  submit(): void {
    if (this.form.invalid || this.loading()) {
      this.form.markAllAsTouched();
      return;
    }
    this.errorMessage.set(null);
    this.loading.set(true);

    const { currentPassword, newPassword } = this.form.getRawValue();
    this.auth.changePassword({ currentPassword, newPassword }).subscribe({
      next: () => {
        this.loading.set(false);
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err, 'Could not change your password. Check your current password and try again.'));
      }
    });
  }
}
