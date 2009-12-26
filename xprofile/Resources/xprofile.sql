CREATE TABLE `XProfile` (
  `UserID` char(36) NOT NULL,
  `ImageID` char(36) NOT NULL default '00000000-0000-0000-0000-000000000000',
  `ProfileText` text,
  `FirstLifeText` text,
  `FirstLifeImageID` char(36) NOT NULL default '00000000-0000-0000-0000-000000000000',
  `ProfileUrl` varchar(255) NOT NULL default '',
  `PartnerID` char(36) NOT NULL default '00000000-0000-0000-0000-000000000000',
  PRIMARY KEY  (`UserID`)
) ENGINE=InnoDB;

CREATE TABLE `XProfileClassifieds` (
  `UserID` char(36) NOT NULL,
  `ClassifiedID` char(36) NOT NULL,
  `CreatorID` char(36) NOT NULL default '00000000-0000-0000-0000-000000000000',
  `CreationDate` char(16) NOT NULL default '0',
  `ExpirationDate` char(16) NOT NULL default '0',
  `Category` char(16) NOT NULL default '0',
  `Name` varchar(255) NOT NULL default '',
  `Description` text,
  `ParcelID` char(36) NOT NULL default '00000000-0000-0000-0000-000000000000',
  `ParentEstate` char(16) NOT NULL default '1',
  `SnapshotID` char(36) NOT NULL default '00000000-0000-0000-0000-000000000000',
  `RegionName` varchar(64) NOT NULL default '',
  `GlobalPosition` varchar(64) NOT NULL default '<0,0,0>',
  `ParcelName` varchar(64) NOT NULL default '',
  `ClassifiedFlags` varchar(16) NOT NULL default '0',
  `Price` varchar(16) NOT NULL default '100',
  PRIMARY KEY  (`UserID`,`ClassifiedID`),
  UNIQUE KEY `ClassifiedID` (`ClassifiedID`),
  KEY `UserID` (`UserID`)
) ENGINE=InnoDB;

CREATE TABLE `XProfileInterests` (
  `UserID` char(36) NOT NULL,
  `WantMask` char(16) NOT NULL default '0',
  `WantText` varchar(255) NOT NULL default '',
  `SkillsMask` char(16) NOT NULL default '0',
  `SkillsText` varchar(255) NOT NULL default '',
  `Languages` varchar(255) NOT NULL default '',
  PRIMARY KEY  (`UserID`)
) ENGINE=InnoDB;

CREATE TABLE `XProfileNotes` (
  `UserID` char(36) NOT NULL,
  `AvatarID` char(36) NOT NULL,
  `Note` text,
  PRIMARY KEY  (`UserID`,`AvatarID`)
) ENGINE=InnoDB;

CREATE TABLE `XProfilePicks` (
  `UserID` char(36) NOT NULL,
  `PickID` char(36) NOT NULL,
  `CreatorID` char(36) NOT NULL default '00000000-0000-0000-0000-000000000000',
  `TopPick` char(16) NOT NULL default 'false',
  `Name` varchar(255) NOT NULL default '',
  `Description` text,
  `SnapshotID` char(36) NOT NULL default '00000000-0000-0000-0000-000000000000',
  `SortOrder` char(16) NOT NULL default '1',
  `Enabled` char(16) NOT NULL default 'true',
  `RegionName` varchar(64) NOT NULL default '',
  `ParcelID` char(36) NOT NULL default '00000000-0000-0000-0000-000000000000',
  `GlobalPosition` varchar(64) NOT NULL default '<0,0,0>',
  `OriginalName` varchar(255) NOT NULL default '',
  `UserName` varchar(64) NOT NULL default '',
  PRIMARY KEY  (`UserID`,`PickID`),
  UNIQUE KEY `PickID` (`PickID`),
  KEY `UserID` (`UserID`)
) ENGINE=InnoDB;
