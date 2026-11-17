/** A person, as the API describes them in the users list. */
export interface UserSummary {
  id: string;
  email: string | null;
  roles: string[];
  /** False for a deactivated person, who can no longer sign in. */
  isActive: boolean;
}
