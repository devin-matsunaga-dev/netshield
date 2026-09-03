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

import type { Permission } from '@/api/types';

/** A destination with no children. */
export interface NavLeaf {
  readonly label: string;
  readonly to: string;
  /**
   * What a session must hold for this destination to appear. Absent means a session is enough.
   *
   * Hiding is presentation and never the boundary — the API refuses the call whether or not the
   * link was drawn (ARCHITECTURE.md §8). It spares a reader a screen they would only be refused.
   */
  readonly permission?: Permission;
}

/** A row in the sidebar: either a destination or a section that expands into destinations. */
export interface NavEntry {
  readonly label: string;
  readonly icon: LucideIcon;
  readonly to?: string;
  readonly permission?: Permission;
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
 *
 * Each entry names the permission that makes it visible (WP-0.7). Overview and Dashboard name
 * none: a session is enough to see them. Three of the mappings are provisional — Security's two
 * children and Threats are screens SPEC.md §2 does not name, so their gates are the most
 * defensible reading rather than a settled one, and the Phase 7 package that names those
 * surfaces should revisit them (recorded in STATUS.md).
 */
export const navigation: readonly NavEntry[] = [
  { label: 'Overview', icon: Home, to: '/overview' },
  { label: 'Dashboard', icon: LayoutDashboard, to: '/dashboard' },
  { label: 'Network', icon: Network, to: '/network', permission: 'TopologyRead' },
  { label: 'Devices', icon: Monitor, to: '/devices', permission: 'InventoryRead' },
  { label: 'Clients', icon: Users, to: '/clients', permission: 'InventoryRead' },
  {
    label: 'Security',
    icon: Shield,
    children: [
      { label: 'Posture', to: '/security/posture', permission: 'ComplianceRead' },
      { label: 'Findings', to: '/security/findings', permission: 'VulnerabilitiesRead' },
    ],
  },
  { label: 'Threats', icon: Bug, to: '/threats', permission: 'AlertsRead' },
  { label: 'Alerts', icon: Bell, to: '/alerts', permission: 'AlertsRead' },
  {
    label: 'Vulnerabilities',
    icon: ShieldAlert,
    to: '/vulnerabilities',
    permission: 'VulnerabilitiesRead',
  },
  { label: 'Compliance', icon: ClipboardCheck, to: '/compliance', permission: 'ComplianceRead' },
  {
    label: 'Reports',
    icon: FileText,
    children: [
      { label: 'Inventory', to: '/reports/inventory', permission: 'ReportsRead' },
      { label: 'Availability', to: '/reports/availability', permission: 'ReportsRead' },
      { label: 'Bandwidth', to: '/reports/bandwidth', permission: 'ReportsRead' },
      { label: 'Compliance', to: '/reports/compliance', permission: 'ReportsRead' },
      { label: 'Vulnerability', to: '/reports/vulnerability', permission: 'ReportsRead' },
      { label: 'Alert activity', to: '/reports/alert-activity', permission: 'ReportsRead' },
    ],
  },
  { label: 'Policies', icon: Scale, to: '/policies', permission: 'PoliciesWrite' },
  { label: 'Logs', icon: ScrollText, to: '/logs', permission: 'LogsRead' },
  {
    label: 'Administration',
    icon: Settings,
    children: [
      { label: 'Users', to: '/administration/users', permission: 'SystemAdminister' },
      { label: 'Roles', to: '/administration/roles', permission: 'SystemAdminister' },
      { label: 'Audit log', to: '/administration/audit-log', permission: 'AuditRead' },
      {
        label: 'System health',
        to: '/administration/system-health',
        permission: 'SystemAdminister',
      },
      {
        label: 'Backup and restore',
        to: '/administration/backup-restore',
        permission: 'SystemAdminister',
      },
      {
        label: 'License and version',
        to: '/administration/license',
        permission: 'SystemAdminister',
      },
    ],
  },
];

/** Every destination the sidebar can reach, sections flattened into their children. */
export const navigationDestinations: readonly string[] = navigation.flatMap((entry) =>
  entry.children ? entry.children.map((child) => child.to) : entry.to ? [entry.to] : [],
);
