export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface DailyActivityPoint {
  date: string
  activeSeconds: number
  idleSeconds: number
  unknownSeconds: number
}

export interface ApplicationUsage {
  processName: string
  foregroundSeconds: number
  backgroundSeconds: number
  sessionCount: number
  share: number
}

export interface DepartmentKpi {
  departmentId: string | null
  departmentName: string
  productivityScore: number | null
  coverage: number
  activeSeconds: number
  idleSeconds: number
}

export interface DashboardSummary {
  totalDevices: number
  onlineDevices: number
  offlineDevices: number
  pendingDevices: number
  blockedDevices: number
  monitoredSecondsToday: number
  activeApplicationsToday: number
  policyFailures: number
  devicesMissingData: number
  openAlerts: number
  activityByDay: DailyActivityPoint[]
  topApplications: ApplicationUsage[]
  departmentKpis: DepartmentKpi[]
  generatedAt: string
}

export type DeviceStatus = 'Pending' | 'Active' | 'Blocked' | 'Revoked'

export interface Device {
  id: string
  displayName: string
  hostname: string | null
  osVersion: string | null
  agentVersion: string | null
  status: DeviceStatus
  isOnline: boolean
  lastHeartbeatAt: string | null
  employeeId: string | null
  employeeName: string | null
  departmentId: string | null
  departmentName: string | null
  monitoringEnabled: boolean
  appliedPolicyId: string | null
  appliedPolicyVersion: number | null
  createdAt: string
  activatedAt: string | null
  blockReason: string | null
}

export interface ActivationCode {
  deviceId: string
  activationCode: string
  expiresAt: string
}

export interface LiveDevice {
  deviceId: string
  deviceName: string
  employeeName: string | null
  departmentName: string | null
  currentProcessName: string | null
  currentWindowTitle: string | null
  currentState: string | null
  activityStartedAt: string | null
  activityDurationSeconds: number | null
  lastHeartbeatAt: string | null
  connectedAt: string | null
  disconnectedAt: string | null
  onlineSeconds: number | null
  agentStatus: 'Online' | 'Offline' | 'Paused' | 'Blocked'
  monitoringEnabled: boolean
}

export interface DeviceConnection {
  connectedAt: string
  disconnectedAt: string | null
  durationSeconds: number | null
}

export interface Employee {
  id: string
  fullName: string
  employeeNumber: string
  email: string | null
  position: string | null
  departmentId: string | null
  departmentName: string | null
  workScheduleId: string | null
  isActive: boolean
  isDemo: boolean
  deviceCount: number
}

export interface Department {
  id: string
  name: string
  branch: string | null
  employeeCount: number
  isDemo: boolean
}

export type Weekday = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday'

export interface WorkSchedule {
  id: string
  name: string
  timeZoneId: string
  workDays: Weekday[]
  workStart: string
  workEnd: string
  breakStart: string | null
  breakEnd: string | null
  isDefault: boolean
}

export interface TimelineItem {
  id: string
  deviceId: string
  deviceName: string
  kind: 'Application' | 'Idle' | 'Paused' | 'PolicyViolation'
  processName: string | null
  windowTitle: string | null
  startedAt: string
  endedAt: string
  durationSeconds: number
}

export type Classification = 'Neutral' | 'Approved' | 'NotApproved'
export type Restriction = 'None' | 'Block'
export type PolicyMode = 'AuditOnly' | 'Enforce'
export type TitleCapture = 'None' | 'Masked' | 'Full'
export type AssignmentScope = 'AllDevices' | 'Department' | 'Device'

export interface ApplicationSummary {
  processName: string
  foregroundSeconds: number
  backgroundSeconds: number
  deviceCount: number
  classification: Classification | 'Mixed' | 'Unclassified'
}

export interface ApplicationRule {
  id: string
  policyId: string
  name: string
  processName: string
  pathPattern: string | null
  publisher: string | null
  classification: Classification
  restriction: Restriction
  dailyLimitMinutes: number | null
  requirePassword: boolean
  description: string | null
}

export interface SupportMessage {
  id: string
  deviceId: string
  sender: 'Device' | 'Admin'
  senderName: string | null
  text: string
  createdAt: string
  readAt: string | null
}

export interface SupportThread {
  deviceId: string
  deviceName: string
  employeeName: string | null
  unreadFromDevice: number
  lastMessageAt: string | null
  lastMessagePreview: string | null
}

export interface PolicyAssignment {
  scope: AssignmentScope
  departmentId: string | null
  deviceId: string | null
}

export interface Policy {
  id: string
  name: string
  description: string | null
  version: number
  publishedVersion: number | null
  status: 'Draft' | 'Published' | 'Archived'
  mode: PolicyMode
  priority: number
  enforcementApproved: boolean
  hasUnpublishedChanges: boolean
  effectiveFrom: string
  effectiveTo: string | null
  workScheduleId: string | null
  idleThresholdSeconds: number
  samplingIntervalSeconds: number
  heartbeatIntervalSeconds: number
  windowTitleCapture: TitleCapture
  trackBackgroundProcesses: boolean
  screenshotsEnabled: boolean
  assignments: PolicyAssignment[]
  rules: ApplicationRule[]
  versions: { version: number; note: string | null; createdAt: string; createdByUserId: string | null }[]
  results: { pending: number; applied: number; failed: number; unsupported: number; requiresAdministratorApproval: number }
  createdAt: string
  updatedAt: string
  publishedAt: string | null
}

export interface ProductivityMetric {
  key: string
  label: string
  value: number | null
  weight: number
  formula: string
  missingReason: string | null
}

export interface ReportRow {
  groupKey: string
  groupName: string
  position: string | null
  departmentName: string | null
  deviceCount: number
  scheduledSeconds: number
  activeSeconds: number
  idleSeconds: number
  pausedSeconds: number
  scheduledBreakSeconds: number
  outsideScheduleSeconds: number
  unknownSeconds: number
  coverage: number
  confidence: 'High' | 'Medium' | 'Low' | 'None'
  productivityScore: number | null
  isReliable: boolean
  metrics: ProductivityMetric[]
  topApplications: ApplicationUsage[]
}

export interface Report {
  period: string
  from: string
  to: string
  timeZone: string
  groupBy: 'employee' | 'department' | 'device'
  formula: { approvedUsageWeight: number; scheduleAdherenceWeight: number; reportedActivityWeight: number; minimumCoverage: number; description: string }
  rows: ReportRow[]
  daily: DailyActivityPoint[]
  disclaimer: string
}

export interface Alert {
  id: string
  deviceId: string | null
  deviceName: string | null
  type: 'DeviceOffline' | 'PolicyFailed' | 'PolicyViolation' | 'TelemetryGap' | 'Security'
  severity: 'Info' | 'Warning' | 'Critical'
  message: string
  createdAt: string
  acknowledgedAt: string | null
  resolvedAt: string | null
}

export interface AuditLog {
  id: string
  occurredAt: string
  actorType: 'User' | 'Device' | 'System' | 'Anonymous'
  actorUserId: string | null
  actorEmail: string | null
  actorDeviceId: string | null
  action: string
  entityType: string | null
  entityId: string | null
  success: boolean
  ipAddress: string | null
  details: string | null
}

export interface AdminUser {
  id: string
  email: string
  displayName: string
  isActive: boolean
  mfaEnabled: boolean
  roles: string[]
  departmentScopeId: string | null
  lastLoginAt: string | null
  createdAt: string
}

export interface Role {
  id: string
  name: string
  description: string
  permissions: string[]
  isSystem: boolean
  userCount: number
  permissionsLocked: boolean
}

export interface PermissionInfo {
  key: string
  group: string
  assignable: boolean
}

export interface AgentRelease {
  version: string
  buildDate: string
  fileName: string
  size: number
  sha256: string
  updatedAt: string
  source: string
}

export interface SystemSettings {
  telemetryRetentionDays: number
  heartbeatRetentionDays: number
  auditRetentionDays: number
  offlineThresholdSeconds: number
  organizationTimeZone: string
  monitoringNotice: string
  supportContact: string
  approvedUsageWeight: number
  scheduleAdherenceWeight: number
  reportedActivityWeight: number
  minimumCoverage: number
  agentAutoUpdate: boolean
}
