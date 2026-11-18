// These interfaces describe the JSON the API sends and expects.
// They mirror the C# DTOs in ResolveIQ.Application/DTOs/Auth (property names become camelCase in JSON).

export type Role = 'Admin' | 'Manager' | 'Employee';

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
}

/** What /api/auth/login and /api/auth/register return. */
export interface AuthResponse {
  token: string;
  /** A single-use ticket for getting a new token when this one expires. Missing in sessions saved by older versions. */
  refreshToken?: string;
  email: string;
  roles: Role[];
}

/** What /api/auth/registration says: whether people can create their own account, and for which email domains. */
export interface RegistrationStatus {
  enabled: boolean;
  allowedEmailDomains: string[];
}
