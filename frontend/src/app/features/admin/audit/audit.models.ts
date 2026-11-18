export type AuditAction =
  | 'user.created'
  | 'user.registered'
  | 'user.role_changed'
  | 'user.deactivated'
  | 'user.reactivated'
  | 'user.reset_email_sent'
  | 'category.created'
  | 'category.updated'
  | 'sla.updated';

/** Every action, with the words people see for it. */
export const AUDIT_ACTIONS: { value: AuditAction; label: string }[] = [
  { value: 'user.created', label: 'User created' },
  { value: 'user.registered', label: 'User signed up' },
  { value: 'user.role_changed', label: 'Role changed' },
  { value: 'user.deactivated', label: 'User deactivated' },
  { value: 'user.reactivated', label: 'User reactivated' },
  { value: 'user.reset_email_sent', label: 'Password reset email sent' },
  { value: 'category.created', label: 'Category created' },
  { value: 'category.updated', label: 'Category changed' },
  { value: 'sla.updated', label: 'SLA policy changed' },
];

export function auditActionLabel(action: string): string {
  return AUDIT_ACTIONS.find((a) => a.value === action)?.label ?? action;
}

export interface AuditLine {
  id: string;
  createdAtUtc: string;
  actorEmail: string | null;
  action: AuditAction;
  targetType: string;
  targetLabel: string | null;
  details: string | null;
}

export interface AuditPage {
  items: AuditLine[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
