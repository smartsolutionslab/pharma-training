#!/bin/bash
# kcadm.sh's sign-in with a one-time code for Keycloak's administrators (#278): see KcadmOtp.java beside it.
# Runs the program from its source with Keycloak's own libraries, which hold the JSON parser it uses.
# A terminal for input but not for output means there is no console: the password would be read with echo and left on
# the screen, and the key would go wherever the output went. Refuse rather than ask (#331).
if [ -t 0 ] && [ ! -t 1 ]; then
  echo "kcadm-otp.sh: run it with its output on the terminal; redirected, the password would echo and the key would leave the screen." >&2
  exit 2
fi

exec java -cp '/opt/keycloak/lib/lib/main/*' /opt/keycloak/bin/KcadmOtp.java "$@"
