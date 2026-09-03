import {
  Bell,
  Bug,
  ClipboardCheck,
  FileText,
  Users,
  LayoutDashboard,
  type LucideIcon,
  Monitor,
  Network,
  Home,
  Scale,
  ScrollText,
  Settings,
  Shield,
  ShieldAlert,
} from 'lucide-react';

/** A destination with no children. */
export interface NavLeaf {
  readonly label: string;
  readonly to: string;
}

/** A row in the sidebar: either a destination or a section that expands into destinations. */
export interface NavEntry {
  readonly label: string;
  readonly icon: LucideIcon;
  readonly to?: string;
  readonly children?: readonly NavLeaf[];
}

/**
 * The sidebar, in the order the reference screenshot puts it in
 * (`docs/design/reference-dashboard.png`, which is the source of truth for this).
 *
 * Three rows are sections rather than destinations. The screenshot shows their chevrons but not
 * what is behind them; Reports and Administration are the areas SPEC.md §2 names, and Security's
 * two are placeholders for the security posture surface, which no work package has specified
 * yet. Clicking a section expands it and navigates nowhere — the destinations are its children.
 */
export const navigation: readonly NavEntry[] = [
  { label: 'Overview', icon: Home, to: '/overview' },
  { label: 'Dashboard', icon: LayoutDashboard, to: '/dashboard' },
  { label: 'Network', icon: Network, to: '/network' },
  { label: 'Devices', icon: Monitor, to: '/devices' },
  { label: 'Clients', icon: Users, to: '/clients' },
  {
    label: 'Security',
    icon: Shield,
    children: [
      { label: 'Posture', to: '/security/posture' },
      { label: 'Findings', to: '/security/findings' },
    ],
  },
  { label: 'Threats', icon: Bug, to: '/threats' },
  { label: 'Alerts', icon: Bell, to: '/alerts' },
  { label: 'Vulnerabilities', icon: ShieldAlert, to: '/vulnerabilities' },
  { label: 'Compliance', icon: ClipboardCheck, to: '/compliance' },
  {
    label: 'Reports',
    icon: FileText,
    children: [
      { label: 'Inventory', to: '/reports/inventory' },
      { label: 'Availability', to: '/reports/availability' },
      { label: 'Bandwidth', to: '/reports/bandwidth' },
      { label: 'Compliance', to: '/reports/compliance' },
      { label: 'Vulnerability', to: '/reports/vulnerability' },
      { label: 'Alert activity', to: '/reports/alert-activity' },
    ],
  },
  { label: 'Policies', icon: Scale, to: '/policies' },
  { label: 'Logs', icon: ScrollText, to: '/logs' },
  {
    label: 'Administration',
    icon: Settings,
    children: [
      { label: 'Users', to: '/administration/users' },
      { label: 'Roles', to: '/administration/roles' },
      { label: 'Audit log', to: '/administration/audit-log' },
      { label: 'System health', to: '/administration/system-health' },
      { label: 'Backup and restore', to: '/administration/backup-restore' },
      { label: 'License and version', to: '/administration/license' },
    ],
  },
];

/** Every destination the sidebar can reach, sections flattened into their children. */
export const navigationDestinations: readonly string[] = navigation.flatMap((entry) =>
  entry.children ? entry.children.map((child) => child.to) : entry.to ? [entry.to] : [],
);
