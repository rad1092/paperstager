- [x] Dependencies + toolchain
- [x] Core + adversarial decoded-object safety tests
- [x] Desktop workflow + headless tests
- [x] Native internal GUI automation + actual displayed-client renders
- [x] Documentation + license audit + scans
- [x] Exact-SHA multi-OS candidate CI + package launch/restart (5e761b3, run 37720078722)
- [x] Tag-gated release and public package re-download/checksum verification implemented

Final publication result is recorded by the v0.1.0 tag workflow and release notes; all three native jobs must pass before publication.

External native file-picker/OS drag-and-drop manual checks are unverified because CUA app access did not return; no security/permission changes attempted.
