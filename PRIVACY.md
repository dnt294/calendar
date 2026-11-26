# Privacy Policy — personal-calendar

_Last updated: 2026-11-26_

personal-calendar is a Windows desktop calendar client built and used by a single person (the developer) for personal use. It is not distributed to other users.

## What data the app accesses

- With your permission (Google OAuth, scope `https://www.googleapis.com/auth/calendar`), the app reads and writes events of the calendars in the Google account you sign in with: titles, times, locations, descriptions, attendees, reminders and your RSVP status.
- It does this only to show your calendars and to create, edit, delete or respond to events at your request.

## Where data is stored

- Calendar data is cached **only on your own computer** (a local SQLite file in the app's data folder) so the app works offline.
- OAuth tokens are stored **only on your own computer**, encrypted with Windows DPAPI (current user).
- Nothing is sent to any server other than Google's own APIs. There is no analytics, telemetry, advertising or third-party sharing.

## Removing your data

- Removing an account in the app deletes its cached data and tokens from your computer.
- Uninstalling the app deletes all of its local data.
- You can revoke the app's access at any time at https://myaccount.google.com/permissions.

## Google API Services User Data Policy

The app's use of information received from Google APIs adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements.

## Contact

Open an issue on the project repository, or contact the developer through the support email shown on the Google consent screen.
