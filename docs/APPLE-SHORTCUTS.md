# Apple Shortcut setup

The token displayed by Bluetooth Off is a credential. Put it only in the `Authorization` header. Never put it in the URL, a query parameter, the Shortcut name, or a screenshot.

## Turn Off PC Bluetooth

1. Install Tailscale on the iPhone and sign in with the same account used on Windows.
2. In **Shortcuts**, create a shortcut named **Turn Off PC Bluetooth**.
3. Add Tailscale's **Get Status** action.
4. Add an **If** action: if Tailscale is not connected, run Tailscale's **Connect** action.
5. Add **Show Alert** with a confirmation such as: `Turn off Bluetooth on the PC? Connected Bluetooth devices will disconnect.`
6. Add a **URL** action containing:

   ```text
   https://bluetooth-off-pc.<your-tailnet>.ts.net/api/v1/bluetooth/off
   ```

7. Add **Get Contents of URL**:
   - Method: `POST`
   - Request body: none
   - Header: `Authorization`
   - Value: `Bearer YOUR_TOKEN`
8. Read the `state` value from the returned dictionary and show it with **Show Notification**.
9. Test the Shortcut while the PC is awake. Only do this when turning Bluetooth off will not disconnect your only keyboard or mouse.

## Check PC Bluetooth

Create a second shortcut named **Check PC Bluetooth** with the same Tailscale connection and Authorization header, but use:

```text
https://bluetooth-off-pc.<your-tailnet>.ts.net/api/v1/status
```

Set **Get Contents of URL** to `GET`, then show the returned `state`.

## Siri and Home Screen

Both Shortcuts can be added to the Home Screen, a widget, or invoked by Siri. Keep the confirmation alert in the off Shortcut to prevent accidental disconnection.

## Credential safety

- Do not share or export either configured Shortcut; the bearer token is embedded in it.
- Shortcut syncing may copy the token to other devices signed into the same Apple account.
- If the phone or Shortcut may have been exposed, use **Rotate phone token** from the Windows tray menu and update both Shortcuts.
- The old token stops working immediately after rotation.

Apple documents POST API requests in [Request your first API in Shortcuts](https://support.apple.com/guide/shortcuts/apd58d46713f/ios). Tailscale documents its native Connect and Get Status actions in [macOS and iOS shortcuts](https://tailscale.com/docs/features/mac-ios-shortcuts).

