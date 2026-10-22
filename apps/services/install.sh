#!/usr/bin/env bash
# Installs the APRS.NET services on a Linux machine with systemd, such as a Raspberry Pi or a small PC: aprscli as a
# .NET tool in /opt/aprsnet, the aprsnet-serve@ and aprsnet-tnc-share@ services, the aprsnet-check and aprsnet-logs
# scripts, and /etc/aprsnet with example settings. Run it again to upgrade aprscli; it restarts the services that are
# running, and never changes your settings or starts one that isn't.
#
#   sudo ./install.sh                          the latest aprscli, prereleases included
#   sudo ./install.sh --version 0.6.0-alpha.2  that version
#   sudo ./install.sh --source ../../artifacts packages you built: dotnet pack -c Release -o artifacts
#   sudo ./install.sh --dotnet ~/.dotnet/dotnet  a .NET that isn't on root's PATH
#   sudo ./install.sh --no-restart             upgrade without restarting the running services
#   sudo ./install.sh --uninstall              remove it all but /etc/aprsnet and the aprsnet user
#
# Needs the .NET 10 SDK, which `dotnet tool install` uses, where the aprsnet user can reach it.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
package=AprsNet.AprsIsCli
tool_path=/opt/aprsnet
etc=/etc/aprsnet
state=/var/lib/aprsnet
units=/etc/systemd/system
bin=/usr/local/bin
user=aprsnet

version='' source='' dotnet='' uninstall='' restart=1
while (($#)); do
  case "$1" in
    --version) version=${2:?--version needs a version}; shift 2 ;;
    --source) source=$(realpath "${2:?--source needs a folder}"); shift 2 ;;
    --dotnet) dotnet=${2:?--dotnet needs the path to dotnet}; shift 2 ;;
    --no-restart) restart=''; shift ;;
    --uninstall) uninstall=1; shift ;;
    -h | --help) sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "install.sh: unknown option '$1'. Try --help." >&2; exit 2 ;;
  esac
done

say() { printf '==> %s\n' "$*"; }
fail() { printf 'install.sh: %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || fail "run it as root: sudo $0 $*"
command -v systemctl >/dev/null || fail "this needs systemd."

# Every aprsnet service instance that is loaded or enabled, such as aprsnet-tnc-share@tncx.service.
instances() {
  {
    systemctl list-units --all --plain --no-legend 'aprsnet-*@*.service' | awk '{ print $1 }'
    find "$units" -path '*.wants/aprsnet-*@*.service' -printf '%f\n' 2>/dev/null
  } | sort -u
}

if [[ -n $uninstall ]]; then
  for unit in $(instances); do
    say "Stopping and disabling $unit"
    systemctl disable --now "$unit" || true
  done

  say "Removing the services, the scripts and $tool_path"
  rm -f "$units/aprsnet-serve@.service" "$units/aprsnet-tnc-share@.service"
  rm -f "$bin/aprsnet-check" "$bin/aprsnet-logs"
  rm -rf "$tool_path"
  systemctl daemon-reload
  say "Done. Your settings are still in $etc. To remove them and the aprsnet user too:"
  echo "    sudo rm -r $etc $state && sudo userdel $user"
  exit 0
fi

# .NET: the one given, or the first found. Its folder is DOTNET_ROOT for the services.
candidates=("$dotnet")
if [[ -z $dotnet ]]; then
  candidates=("$(command -v dotnet || true)" /usr/bin/dotnet /usr/lib/dotnet/dotnet /usr/share/dotnet/dotnet
    /usr/local/share/dotnet/dotnet /opt/dotnet/dotnet)
  if [[ -n ${SUDO_USER:-} ]]; then
    candidates+=("$(getent passwd "$SUDO_USER" | cut -d: -f6)/.dotnet/dotnet")
  fi
fi

dotnet=''
for candidate in "${candidates[@]}"; do
  if [[ -n $candidate && -x $candidate ]]; then
    dotnet=$(readlink -f "$candidate")
    break
  fi
done

[[ -n $dotnet ]] || fail "can't find .NET. Install the .NET 10 SDK, or give its path with --dotnet."
dotnet_root=$(dirname "$dotnet")
"$dotnet" --list-runtimes | grep -q '^Microsoft\.NETCore\.App 10\.' ||
  fail "$dotnet has no .NET 10 runtime. Install the .NET 10 SDK."
[[ -n $("$dotnet" --list-sdks) ]] || fail "$dotnet has no SDK, which 'dotnet tool install' needs. Install the .NET 10 SDK."
say "Using .NET in $dotnet_root"

if ! getent group dialout >/dev/null; then
  say "Adding the dialout group, which serial ports belong to"
  groupadd --system dialout
fi

if ! id "$user" &>/dev/null; then
  say "Adding the $user system user, in the dialout group"
  useradd --system --user-group --groups dialout --home-dir "$state" --no-create-home \
    --shell "$(command -v nologin || echo /usr/sbin/nologin)" --comment "APRS.NET services" "$user"
elif ! id -nG "$user" | grep -qw dialout; then
  usermod --append --groups dialout "$user"
fi

runuser -u "$user" -- test -x "$dotnet" ||
  fail "the $user user can't run $dotnet; a home folder is usually closed to other users. Install .NET where every user can reach it, such as /usr/share/dotnet, and give it with --dotnet."

arguments=(--tool-path "$tool_path")
if [[ -n $version ]]; then
  arguments+=(--version "$version")
else
  arguments+=(--prerelease)
fi

if [[ -n $source ]]; then
  arguments+=(--add-source "$source")
fi

if [[ -x $tool_path/aprscli ]]; then
  before=$("$tool_path/aprscli" --version 2>/dev/null || echo unknown)
  say "Updating aprscli $before in $tool_path"
  [[ -n $version ]] && arguments+=(--allow-downgrade)
  verb=update
else
  before=''
  say "Installing aprscli in $tool_path"
  verb=install
fi

# Root's own NuGet cache and .NET settings, not those of the user who ran sudo.
HOME=/root DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$dotnet" tool "$verb" "$package" "${arguments[@]}"
after=$(DOTNET_ROOT=$dotnet_root "$tool_path/aprscli" --version)

say "Writing $etc"
install -d -m 755 "$etc" "$etc/serve" "$etc/tnc-share" "$etc/examples"
install -m 644 "$here"/config/*.toml "$etc/examples/"
printf '# Written by install.sh: where .NET is, for %s/aprscli.\nDOTNET_ROOT=%s\n' "$tool_path" "$dotnet_root" \
  >"$etc/environment"
chmod 644 "$etc/environment"
for file in secrets.env otel.env; do
  if [[ ! -e $etc/$file ]]; then
    install -m 600 -o root -g root "$here/config/$file" "$etc/$file"
  fi
done

say "Installing the services and the scripts"
install -m 644 "$here"/systemd/*.service "$units/"
install -m 755 "$here/scripts/aprsnet-check" "$here/scripts/aprsnet-logs" "$bin/"
systemctl daemon-reload

running=()
for unit in $(instances); do
  if systemctl is-active --quiet "$unit"; then
    running+=("$unit")
  fi
done

if ((${#running[@]})) && [[ $before != "$after" ]]; then
  if [[ -n $restart ]]; then
    say "Restarting ${running[*]} on aprscli $after"
    systemctl restart "${running[@]}"
  else
    say "Still running the old aprscli: ${running[*]}. Restart them to use $after."
  fi
fi

say "aprscli $after is installed."
if [[ -z $before ]]; then
  cat <<EOF

Nothing runs yet. To share a TNC and serve what it hears:

    sudo cp $etc/examples/tncx.toml $etc/tnc-share/tncx.toml        # then edit it
    sudo cp $etc/examples/gateway.toml $etc/serve/gateway.toml      # then edit it
    sudo aprsnet-check --config-only
    sudo systemctl enable --now aprsnet-tnc-share@tncx aprsnet-serve@gateway
    sudo aprsnet-check
    aprsnet-logs -f

An IGate's passcode goes in $etc/secrets.env, and an OpenTelemetry Collector's address in $etc/otel.env.
EOF
fi
