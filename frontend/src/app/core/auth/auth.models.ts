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
  email: string;
  roles: Role[];
}
