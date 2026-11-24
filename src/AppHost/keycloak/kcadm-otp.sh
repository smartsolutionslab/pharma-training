#!/bin/bash
# kcadm.sh's sign-in with a one-time code for Keycloak's administrators (#278): see KcadmOtp.java beside it.
# Runs the program from its source with Keycloak's own libraries, which hold the JSON parser it uses.
exec java -cp '/opt/keycloak/lib/lib/main/*' /opt/keycloak/bin/KcadmOtp.java "$@"
