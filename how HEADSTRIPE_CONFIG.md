[33mcommit 1827d7dfab81d124b53d9544e1665b902d83867c[m
Author: david-kolundzic <david-kolundzic@outlook.com>
Date:   Wed May 20 00:51:32 2026 +0200

    Remove Stripe integration and related functionality
    
    Completely removed all Stripe-related services, controllers, models, tests, and configurations from the application. Updated `EvidenceAppendService` comments for clarity and replaced localized terms with English equivalents. Removed Stripe-specific dependencies from project files and configurations.
    
    Refactored `DbResetSmokeTests` and test infrastructure to exclude Stripe-related entities. Simplified `Program.cs` by removing Stripe service registrations and rate-limiting. Deleted unused files like `checkout-test.html` and cleaned up redundant code and namespaces.
    
    Ensured database migrations are applied automatically on startup. Improved code formatting and consistency across the codebase.

[33mcommit 05d1d8f35f86ec7a1b59e71802d67cf99e06f927[m
Author: david-kolundzic <david-kolundzic@outlook.com>
Date:   Tue Feb 24 19:24:44 2026 +0100

    Refactor Stripe webhook to canonical API pipeline
    
    - Switch to single canonical pipeline using Stripe API fetch
    - Move legacy parsing methods to StripeWebhookProcessor.Legacy.cs and mark as [Obsolete]
    - Add StripePayloadExtractor for ID extraction and evidence snapshots
    - Configure Stripe API keys via StripeOptions and appsettings
    - Update docs for migration, config, and testing
    - Legacy code remains for reference; safe to delete after prod verification

[33mcommit 41dd6180e3917c7685e753da3bf922be5b265659[m
Author: david-kolundzic <david-kolundzic@outlook.com>
Date:   Fri Feb 13 10:39:35 2026 +0100

    Add project files.
