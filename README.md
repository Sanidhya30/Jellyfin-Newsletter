# Note

This repository is a maintained fork of the [Jellyfin Newsletter Plugin](https://github.com/Cloud9Developer/Jellyfin-Newsletter-Plugin), originally created by [Cloud9Developer](https://github.com/Cloud9Developer). As the original repository is no longer actively maintained, this introduces several improvements, new features, and bug fixes, including:

* Discord Webhook Support
* Removal of Imgur and Local Hosted Image Dependencies
* TMDB Integration
* Multiple Bug Fixes and Enhancements

# Jellyfin Newsletter Plugin

<p align='center'>
    <img src='https://github.com/Cloud9Developer/Jellyfin-Newsletter-Plugin/blob/master/logo.png?raw=true'/><br>
</p>
This is my first end-to-end C# project, but I hope you enjoy!

# Description

This plugin automacially scans a users library (default every 4 hours), populates a list of *recently added (not previously scanned)* media, converts that data into HTML format, and sends out newsletter to every client(currently email and discord webhook is supported).

<h2 align="center">Email Screenshot</h2>
<p align="center">
    <img src="https://github.com/Cloud9Developer/Jellyfin-Newsletter-Plugin/blob/master/NewsletterExample.png?raw=true" alt="Email Screenshot"/>
</p>

<h2 align="center">Discord Screenshots</h2>
<p align="center" style="display: flex; justify-content: center; gap: 20px;">
    <img src="https://github.com/user-attachments/assets/e6921c80-a904-452e-837c-7306085a9d0f" 
         alt="Discord Screenshot 1" 
         style="height: 300px; object-fit: contain;"/>
    <img src="https://github.com/user-attachments/assets/0f224065-4923-4725-8732-8fcbb07ed359" 
         alt="Discord Screenshot 2" 
         style="height: 300px; object-fit: contain;"/>
</p>

# File Structure

To ensure proper images are being pulled from Jellyfin's database, ensure you follow the standard Organization Scheme for naming and organizing your files. https://jellyfin.org/docs/general/server/media/books

If this format isn't followed properly, Jellyfin may have issue correctly saving the item's data in the proper database (the database that this plugin uses).

```
Shows
├── Series (2010)
│   ├── Season 00
│   │   ├── Some Special.mkv
│   │   ├── Episode S00E01.mkv
│   │   └── Episode S00E02.mkv
│   ├── Season 01
│   │   ├── Episode S01E01-E02.mkv
│   │   ├── Episode S01E03.mkv
│   │   └── Episode S01E04.mkv
│   └── Season 02
│       ├── Episode S02E01.mkv
│       ├── Episode S02E02.mkv
│       ├── Episode S02E03 Part 1.mkv
│       └── Episode S02E03 Part 2.mkv
└── Series (2018)
    ├── Episode S01E01.mkv
    ├── Episode S01E02.mkv
    ├── Episode S02E01-E02.mkv
    └── Episode S02E03.mkv

Movies
├── Film (1990).mp4
├── Film (1994).mp4
├── Film (2008)
│   └── Film.mkv
└── Film (2010)
    ├── Film-cd1.avi
    └── Film-cd2.avi
```

# Testing/Run Frequency

Testing and Frequency can be managed through your Dashboard > Scheduled Tasks

- There are 2 scheduled tasks:
  - Newsletter: Which generates and sends out the newsletters via the clients configured in the plugin from the data scanned from the task below
  - Filesystem Scraper:  Which scans your library, parses the data, and gets it ready for the email

# Installation

Manifest is up an running! You can now import the manifest in Jellyfin and this plugin will appear in the Catalog!

- Go to "Plugins" on your "Dashboard"
- Go to the "Repositories" tab
- Click the '+' to add a new Repository
  - Give it a name (i.e. Newsletters)
  - In "Repository URL," put "https://raw.githubusercontent.com/Sanidhya30/Jellyfin-Newsletter/master/manifest.json"
  - Click "Save"
- You should now see Jellyfin Newsletters in Catalog under the Category "Newsletters"
- Once installed, restart Jellyfin to activate the plugin and configure your settings for the plugin

# Configuration

**Note** :- In previous version of Jellyfin Newsletter imgur or JF server was used for the poster image. This is now deprecated as we have moved to the tmdb api for fetching the poster image internally.

## General Config

### Server URL

- The server url of your jellyfin. This will be used for direct link in discord webhook.

### Library Selection

- Select the item types you want to scan
  - NOTE: this is Item types, not libraries

## Newsletter HTML Format

Allows for use of custom HTML formatting for emails! Defaults to original formatting, but can be modified now!

For defaults, see `Jellyfin.Plugin.Newsletters/Templates/`

### Body HTML

- The main body of your email

### EntryData HTML

- The formatting for each individual entry/series/movie that was found and will be sent out

## Email & SMTP Config

### To Addresses:

- Recipients of the newsletter. Add add as many emails as you'd like, separated by commas.
  - All emails will be sent out via BCC

### From Address

- The address recipients will see on emails as the sender
  - Defaults to JellyfinNewsletter@donotreply.com

### Subject

- The subject of the email

### Smtp Server Address

- The email server address you want to use.
  - Defaults to smtp.gmail.com

### Smtp Port

- The port number used by the email server above
  - Defaults to gmail's port (587)

### Smtp Username

- Your username/email to authenticate to the SMTP server above

### Smtp Password

- Your password to authenticate to the SMTP server above
  - I'm not sure about other email servers, but google requires a Dev password to be created.
    - For gmail specific instructions, you can visit https://support.google.com/mail/answer/185833?hl=en for details

## Discord Config

### Webhook URL:

- Your discord webhook url

### Webhook Name

- Name for your discord webhook, defaults to "Jellyfin Newsletter"

### Fields & Color selection

- Select the fields that you want as part of your embed.
- Select the embed color for each item type.

# Issues

Please leave a ticket in the Issues on this GitHub page and I will get to it as soon as I can.
Please be patient with me, since I did this on the side of my normal job. But I will try to fix any issues that come up to the best of my ability and as fast as I can!

# Available HTML Data Tags

Some of these may not interest that average user (if anyone), but I figured I would have any element in the Newsletters.db be available for use! `<br>`
**NOTE:** *Examples of most tags can be found in the default Templates (template_modern_body.html AND template_modern_entry.html)*

## Required Tags

```
- {EntryData} - Needs to be inside of the 'Body' html
```

## Recommended Tags

```
- {Date} - Auto-generated date of Newsletter email generation
- {SeasonEpsInfo} - This tag is the Plugin-generated Season/Episode data
- {Title} - Title of Movie/Series
- {SeriesOverview} - Movie/Series overview
- {ImageURL} - Poster image for the Movie/Series
- {Type} - Item type (Movie or Series)
- {PremiereYear} - Year Movie/Series was Premiered
- {RunTime} - Movie/Episode Duration (for Series, gives first found duration. Will fix for only single episode or average in future update) 
- {OfficialRating} - TV-PG, TV-13, TV-14, etc.
- {CommunityRating} - Numerical rating stored in Jellyfin's metadata
```

## Non-Recommended Tags

These tags are ***available*** but not recommended to use. Untested behavior using these.

```
- {Filename} - File path of the Movie/Episode (NOT RECOMMENDED TO USE)
- {Season} - Season number of Episode (NOT RECOMMENDED TO USE)
- {Episode} - Episode number (NOT RECOMMENDED TO USE)
- {ItemID} - Jellyfin's assigned ItemID (NOT RECOMMENDED TO USE)
- {PosterPath} - Jellyfin's assigned Poster Path (NOT RECOMMENDED TO USE)
```

## Known Issues

See 'issues' tab in GitHub with the lable 'bug'

# Contribute

If you would like to collaborate/contribute, feel free! Make all PR's to the 'development' branch and please note clearly what was added/fixed, thanks!
