CREATE TABLE Projects(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL UNIQUE COLLATE NOCASE);
CREATE TABLE Ideas(Id INTEGER PRIMARY KEY AUTOINCREMENT, ProjectId INTEGER REFERENCES Projects(Id) ON DELETE SET NULL,
    Content TEXT NOT NULL CHECK(length(trim(Content))>0), CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
    Archived INTEGER NOT NULL DEFAULT 0, Revision INTEGER NOT NULL DEFAULT 1, Type TEXT);
CREATE TABLE Tags(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL UNIQUE COLLATE NOCASE);
CREATE TABLE IdeaTags(IdeaId INTEGER NOT NULL REFERENCES Ideas(Id) ON DELETE CASCADE,
    TagId INTEGER NOT NULL REFERENCES Tags(Id) ON DELETE CASCADE, PRIMARY KEY(IdeaId,TagId));
CREATE TABLE Embeddings(IdeaId INTEGER NOT NULL REFERENCES Ideas(Id) ON DELETE CASCADE, Revision INTEGER NOT NULL,
    Chunk INTEGER NOT NULL, Text TEXT NOT NULL, Model TEXT NOT NULL, Digest TEXT NOT NULL,
    Dimension INTEGER NOT NULL, ChunkerVersion INTEGER NOT NULL, Vector BLOB NOT NULL,
    PRIMARY KEY(IdeaId,Chunk));
CREATE TABLE Settings(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
CREATE TABLE SchemaMigrations(Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL);
CREATE INDEX IX_Ideas_Project ON Ideas(ProjectId,Archived,CreatedAt);
CREATE INDEX IX_IdeaTags_Tag ON IdeaTags(TagId);
CREATE VIRTUAL TABLE IdeasFts USING fts5(Content,content='Ideas',content_rowid='Id',tokenize='unicode61');
CREATE VIRTUAL TABLE IdeasTrigram USING fts5(Content,content='Ideas',content_rowid='Id',tokenize='trigram');
CREATE TRIGGER Ideas_ai AFTER INSERT ON Ideas BEGIN
    INSERT INTO IdeasFts(rowid,Content) VALUES(new.Id,new.Content);
    INSERT INTO IdeasTrigram(rowid,Content) VALUES(new.Id,new.Content); END;
CREATE TRIGGER Ideas_ad AFTER DELETE ON Ideas BEGIN
    INSERT INTO IdeasFts(IdeasFts,rowid,Content) VALUES('delete',old.Id,old.Content);
    INSERT INTO IdeasTrigram(IdeasTrigram,rowid,Content) VALUES('delete',old.Id,old.Content); END;
CREATE TRIGGER Ideas_au AFTER UPDATE OF Content ON Ideas BEGIN
    INSERT INTO IdeasFts(IdeasFts,rowid,Content) VALUES('delete',old.Id,old.Content);
    INSERT INTO IdeasTrigram(IdeasTrigram,rowid,Content) VALUES('delete',old.Id,old.Content);
    INSERT INTO IdeasFts(rowid,Content) VALUES(new.Id,new.Content);
    INSERT INTO IdeasTrigram(rowid,Content) VALUES(new.Id,new.Content); END;
INSERT INTO SchemaMigrations VALUES(1,'2025-01-01T00:00:00.0000000+00:00'); PRAGMA user_version=1;
