#!/usr/bin/with-contenv bashio

export Authentication__Google__ClientId="$(bashio::config 'google_client_id')"
export Authentication__Google__ClientSecret="$(bashio::config 'google_client_secret')"

exec dotnet /app/HouseholdTasks.Server.dll