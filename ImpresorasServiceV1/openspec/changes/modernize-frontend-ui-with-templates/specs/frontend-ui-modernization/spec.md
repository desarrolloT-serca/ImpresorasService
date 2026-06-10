## ADDED Requirements

### Requirement: Unified Visual System for Core Screens
The system SHALL provide a unified visual system across core frontend screens (`dashboard`, `cola`, `impresoras`, `reglas`, `login`) using the existing template assets and consistent UI patterns.

#### Scenario: Consistent layout and navigation
- **WHEN** an authenticated user navigates between core screens
- **THEN** the application shows a consistent sidebar/topbar/content structure and active navigation state across all those screens

#### Scenario: Consistent component presentation
- **WHEN** the user interacts with common UI elements (buttons, forms, tables, alerts, badges)
- **THEN** those elements follow shared visual variants and spacing rules instead of per-view ad hoc styles

### Requirement: Inline Style Elimination in Modernized Screens
The system SHALL avoid inline style definitions in modernized core screens and SHALL use centralized styles/tokens for maintainability.

#### Scenario: Review of migrated views
- **WHEN** a migrated core Blade view is reviewed
- **THEN** color, spacing, radius, and typography styling are referenced through centralized classes or theme tokens, not inline `style` attributes

### Requirement: Dark Mode Visual Coherence
The system SHALL provide a coherent dark mode for modernized core screens with readable contrast and consistent semantic colors.

#### Scenario: Theme switch in core flow
- **WHEN** the user toggles light/dark theme and navigates through core screens
- **THEN** text, surfaces, tables, forms, and alerts remain legible and visually consistent in both themes

### Requirement: No Functional Behavior Regression During UI Upgrade
The system MUST preserve existing business behavior and role-based access while applying the UI modernization.

#### Scenario: Existing workflow compatibility
- **WHEN** users execute existing workflows (login, dashboard navigation, filtering, table actions)
- **THEN** all workflows continue to behave the same functionally as before the visual upgrade
