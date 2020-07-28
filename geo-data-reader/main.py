from geopy.geocoders import Nominatim
import pycountry
import requests
import gzip
import os

geolocator = Nominatim(user_agent="Strava")
api_token = input("Your osm-boundaries API key: ")

# Loop over all countries
for c in pycountry.countries:
    # Fetch country ID
    location = geolocator.geocode(c.name)

    try:
        # Download country geojson file
        r = requests.get(f"https://osm-boundaries.com/Download/Submit?apiKey={api_token}&osmIds=-{location.raw['osm_id']}&recursive&minAdminLevel=2&maxAdminLevel=6&landOnly")

        if r.status_code == 200:
            # Store .gz file
            with open(f"gz/{c.name}.gz", 'wb') as f:
                # Write to disk
                f.write(r.content)

            # Extract files & write geojson to file
            with gzip.open(f"gz/{c.name}.gz", 'rb') as f:
                with open(f"geojson/{c.name}.geojson", "wb") as dump:
                    dump.write(f.read())

            # delete gz
            os.remove(f"gz/{c.name}.gz")
            print(f"Downloaded {c.name}")
        else:
            print("Did not receive HTTP 200")
    except Exception as e:
        print(f"Could not download {e}")
