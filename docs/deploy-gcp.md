# Deploy PMate to Google Cloud (free e2-micro server)

This guide puts PMate on a free Google Cloud server. You then open it on your phone at an HTTPS address.

```
Phone ──HTTPS──▶ Caddy (port 443, gets the certificate itself)
                   │  http://127.0.0.1:5178
                   ▼
                 PMate (Linux build, runs as a systemd service)
                   │
                   ▼
                 SQLite  /var/lib/pmate/pmate.db   daily backups in /var/lib/pmate/backups
```

- **Caddy** is a web server. It gets a free HTTPS certificate from Let's Encrypt and renews it by itself.
- **systemd** is the Linux service manager. It starts PMate at boot and restarts it if it stops.

The free server must be in the US. From Singapore, each action takes about 0.2 seconds to reach it.

The console labels below are shown in Chinese, with English in brackets.

## Before you start

- **The sign-in feature must be finished and committed.** The server is on the public internet. `deploy.ps1` only deploys committed code.
- **Get IT approval** if the data belongs to your company.
- **Use only one database.** After the move, open PMate on your PC through the cloud address too.
- **Windows reminder pop-ups do not work on the server.** Use the Today page instead.

In the commands below, replace these example values with your own:

| Example | Meaning |
|---|---|
| `203.0.113.10` | Your server's external IP address (step 3) |
| `pmate-ray.duckdns.org` | Your domain name (step 4) |

## Step 1: Make an SSH key on your PC

SSH is how your PC logs in to the server and sends new versions. An SSH key is a pair of files. The private key stays on your PC. The public key goes to the server.

Run this in PowerShell. Press Enter twice when it asks for a passphrase (no passphrase):

```powershell
ssh-keygen -t ed25519 -f "$HOME\.ssh\pmate-gcp" -C deploy
```

`-C deploy` makes Google create a server user named `deploy`. Copy the public key to the clipboard for step 2:

```powershell
Get-Content "$HOME\.ssh\pmate-gcp.pub" | Set-Clipboard
```

## Step 2: Create the server

1. In the console, click **＋ 创建虚拟机 (Create VM)**. If it asks you to enable the **Compute Engine API**, click **启用 (Enable)** and wait 1–2 minutes.
2. **机器配置 (Machine configuration):**
   - **名称 (Name):** `pmate`
   - **区域 (Region):** `us-west1 (俄勒冈 Oregon)`. It is the closest free region to Singapore.
   - **可用区 (Zone):** any, for example `us-west1-b`
   - **系列 (Series):** `E2`
   - **机器类型 (Machine type):** `e2-micro`. The right side should say the price is covered by the free tier.
3. **操作系统和存储 (OS and storage) → 更改 (Change):**
   - **操作系统 (Operating system):** `Ubuntu`
   - **版本 (Version):** `Ubuntu 24.04 LTS`, **x86/64** (not Arm, not Minimal)
   - **启动磁盘类型 (Boot disk type):** `标准永久性磁盘 (Standard persistent disk)`. **The default "Balanced" disk is not free.**
   - **大小 (Size):** `30` GB
   - Click **选择 (Select)**.
4. **数据保护 (Data protection):** if this section exists, choose **无备份 (No backups)**. Snapshots cost money.
5. **网络 (Networking):**
   - **防火墙 (Firewall):** leave **允许 HTTP 流量** and **允许 HTTPS 流量** **unticked** for now. Step 9 explains why.
   - Open the network interface (**default**). Under **外部 IPv4 地址 (External IPv4 address)**, choose **保留静态外部 IP 地址 (Reserve static external IP address)**. Name it `pmate-ip`. Otherwise the address can change when the server restarts.
6. **安全 (Security) → 管理访问权限 (Manage access) → 添加 SSH 密钥 / 添加项 (Add item):** paste the public key from step 1.
7. Click **创建 (Create)**. After about 1 minute, the server shows a green check. Copy its **外部 IP (External IP)**.

## Step 3: Set up a free domain name

HTTPS needs a domain name. DuckDNS gives free names.

1. Go to <https://www.duckdns.org> and sign in (with GitHub or Google).
2. Add a sub domain, for example `pmate-ray`. Your address is then `pmate-ray.duckdns.org`.
3. Paste the server's IP into **current ip**. Click **update ip**.

## Step 4: Set a budget alert

This sends you an email if anything starts to cost money.

1. Console search bar: type **预算和提醒 (Budgets & alerts)** and open it.
2. **创建预算 (Create budget).** Set the amount to **1 USD**. Keep the default email alerts. Click **完成 (Finish)**.

## Step 5: Log in to the server

Test the login. Type `yes` when it asks about the fingerprint. Type `exit` to leave.

```powershell
ssh -i "$HOME\.ssh\pmate-gcp" deploy@203.0.113.10
```

If it says **Permission denied**, the public key was not saved. Open the server, click **修改 (Edit)**, and add the key again under **SSH 密钥 (SSH keys)**.

## Step 6: Set up the server

Run these from the repository folder on your PC:

```powershell
scp -i "$HOME\.ssh\pmate-gcp" -r deploy deploy@203.0.113.10:~
ssh -i "$HOME\.ssh\pmate-gcp" deploy@203.0.113.10 "sudo bash ~/deploy/server-setup.sh pmate-ray.duckdns.org"
```

This takes 5–10 minutes on the small server. The script:

- Sets the time zone to Singapore. "Today" and due dates use the server clock.
- Installs security updates and turns on automatic security updates.
- Adds 2 GB of swap space. Swap space is disk space the server uses when its 1 GB of memory is full.
- Installs Caddy.
- Creates a `pmate` user and the folders `/opt/pmate` (program) and `/var/lib/pmate` (data).
- Installs the PMate service and the `pmate-install` command.

## Step 7: Install PMate

Commit your work first. Then run this on your PC:

```powershell
.\scripts\deploy.ps1 -Server deploy@203.0.113.10 -Key "$HOME\.ssh\pmate-gcp"
```

It builds a Linux version, uploads it (about 50 MB), and starts it. It ends with `PMate is running the new version.`

### Move your existing data (optional)

Skip this if you want to start with an empty PMate.

1. Stop PMate on your PC:
   ```powershell
   .\scripts\stop.ps1
   ```
2. Upload the database. SQLite may also have a `pmate.db-wal` file with recent changes. The second line uploads it if it exists.
   ```powershell
   scp -i "$HOME\.ssh\pmate-gcp" "$env:LOCALAPPDATA\PMate\pmate.db" deploy@203.0.113.10:/tmp/
   if (Test-Path "$env:LOCALAPPDATA\PMate\pmate.db-wal") { scp -i "$HOME\.ssh\pmate-gcp" "$env:LOCALAPPDATA\PMate\pmate.db-wal" deploy@203.0.113.10:/tmp/ }
   ```
3. Put it in place on the server:
   ```powershell
   ssh -i "$HOME\.ssh\pmate-gcp" deploy@203.0.113.10 "sudo systemctl stop pmate && sudo sh -c 'rm -f /var/lib/pmate/pmate.db*' && sudo install -o pmate -g pmate -m 600 /tmp/pmate.db* /var/lib/pmate/ && rm -f /tmp/pmate.db* && sudo systemctl start pmate"
   ```

## Step 8: Create your account through a private connection

**Why:** while PMate has no user, the first visitor can create the admin account. Bots find new HTTPS addresses within minutes. So create your account before the site is public.

1. Open an SSH tunnel. An SSH tunnel sends a port on your PC to the server through the SSH connection. Keep this window open:
   ```powershell
   ssh -i "$HOME\.ssh\pmate-gcp" -N -L 5178:127.0.0.1:5178 deploy@203.0.113.10
   ```
   If PMate still runs on your PC, stop it first (`.\scripts\stop.ps1`). It uses the same port.
2. On your PC, open <http://localhost:5178>. Create your admin account with a long password (12+ characters).
   If you moved data that already has an account, sign in to check that it works.
3. Close the tunnel window (Ctrl+C).

## Step 9: Open the site to the internet

1. Console: **Compute Engine → 虚拟机实例 (VM instances) → pmate → 修改 (Edit)**.
2. Under **网络 → 防火墙 (Networking → Firewall)**, tick **允许 HTTP 流量** and **允许 HTTPS 流量**. Click **保存 (Save)**.
3. Tell Caddy to get the certificate now:
   ```powershell
   ssh -i "$HOME\.ssh\pmate-gcp" deploy@203.0.113.10 "sudo systemctl reload caddy"
   ```
4. Wait about 1 minute. Open <https://pmate-ray.duckdns.org> on your phone.

Port 80 is needed to get the certificate. Port 443 is for the site itself.

## Step 10: Set up your phone

1. Sign in and tick **Remember me**, so you stay signed in for 30 days.
2. Add it to the home screen. It then opens like an app:
   - **iPhone (Safari):** Share button → **Add to Home Screen**.
   - **Android (Chrome):** ⋮ menu → **Add to Home screen**.

## Step 11: Upgrade before the trial ends

The free trial ends after 90 days or when the $300 credit is used. If you do not upgrade, Google shuts down the server. After 30 more days, it deletes it.

1. Console search bar: **结算 (Billing)** → **升级 (Upgrade)** → **激活完整账号 (Activate full account)**.
2. The e2-micro server stays free after the upgrade. Your budget alert from step 4 warns you if anything costs money.

Do this soon after the site works, so you don't forget.

## Updating PMate later

Commit your changes. Then run the same command as in step 7:

```powershell
.\scripts\deploy.ps1 -Server deploy@203.0.113.10 -Key "$HOME\.ssh\pmate-gcp"
```

Before it swaps versions, it copies the database to `/var/lib/pmate/backups/pre-deploy-<time>`. If the new version does not start within 60 seconds, the old version is put back.

## Backups

The server makes one backup each day and keeps 14. They are on the same disk as the database. Download a copy to your PC now and then:

```powershell
.\scripts\pull-backup.ps1 -Server deploy@203.0.113.10 -Key "$HOME\.ssh\pmate-gcp"
```

The copies go to `%LOCALAPPDATA%\PMate\cloud-backups`.

## Staying free

| Setting | Must be |
|---|---|
| Machine type | e2-micro, only one |
| Region | us-west1, us-central1 or us-east1 |
| Disk | Standard persistent disk, 30 GB or less |
| Snapshots | none |
| Static IP | attached to the running server. An IP that is reserved but not attached costs money. |
| Data sent out | under 1 GB a month. One person using PMate stays far below this. |

## When something goes wrong

Log in with `ssh` first (step 5). Then:

| Problem | What to check |
|---|---|
| The page does not load | Check that DuckDNS has the right IP. Check that both firewall boxes in step 9 are ticked. |
| Certificate error in the browser | Run `sudo journalctl -u caddy -n 50`. Usually port 80 is closed or DuckDNS has the wrong IP. After a fix, run `sudo systemctl reload caddy`. |
| "Bad Request - Invalid Hostname" | The domain given to `server-setup.sh` is not the one you opened. Run `server-setup.sh` again with the right name. Then run `sudo systemctl restart pmate`. |
| "502 Bad Gateway" | PMate is not running. Run `sudo systemctl status pmate` and `sudo journalctl -u pmate -n 100`. |
| Forgot your password | Another admin can reset it. If you are the only admin, there is no reset yet. Keep the password in a password manager. |

Useful commands on the server:

```bash
sudo systemctl restart pmate      # restart the app
sudo journalctl -u pmate -f       # watch the app log (Ctrl+C to stop)
sudo ls -l /var/lib/pmate/backups # list backups
```
