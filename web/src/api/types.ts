export type Role = 'Owner' | 'Staff';
export type UnitType = 'Piece' | 'Gram';

export interface Me {
  id: number;
  name: string;
  role: Role;
}

export interface UserOption {
  id: number;
  name: string;
}

export interface AuthStatus {
  needsSetup: boolean;
  users: UserOption[];
}
