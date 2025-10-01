# SpeechBlocks-PRG

This repository contains SpeechBlocks-PRG, a fork of SpeechBlocks-II, a child-driven, constructioninst-inspired app for early literacy learning. SpeechBlocks-II and the associated experiments was described in:

Sysoev, I., Gray, J. H., Fine, S., Makini, S. P., & Roy, D. (2022). Child-driven, machine-guided: Automatic scaffolding of constructionist-inspired early literacy play. Computers & Education, 182, 104434.

If you use parts of this repo for your own research work, citation of that paper would be appreciated :)

The app was developed in Unity.

## Third-party components / How to build this repo

The app uses a number of third-party components that I couldn't include in the repo:

- A Firebase Unity plugin;
- Cloud services: Firebase, Azure TTS and Google Speech API - for configuring devices / cloud sourage, speech synthesis and speech recognition respectively;
- A collection of about 2000 images from Flaticon.com, which are the building blocks of the scenes kids create in SpeechBlocks.

To install Firebase plugin, download Firebase Unity SDK (https://firebase.google.com/download/unity). In Unity, navigate to "Assets > Import Package > Custom Package". Import FirebaseDatabase.unitypackage from the SDK. The rest of the packages aren't needed.

To activate cloud services, you will need to subscribe to them and insert the corresponding authentication info into Assets/Config/KeyConfig.json. Note that the app has an offline mode (in case kids experience loss of wifi), and therefore it can work without these services. In the offline mode, it would use Android speech synthesizer. However, the quality of speech would become significantly lower. And, of course, the users won't be able to select whatever they want to build using speech recognition, which is one of the key features of the app.

To get the images, please contact me at isysoev@alum.mit.edu. Once received, unpack the archive into Assets/Resources/Images.

## Differences from the version in the paper

This version of the app doesn't exactly correspond to what was described in the paper, but is a result of about two years of extra development. Here are the key changes:

1. The original study involved a lot of experimentation with different modes of scaffolding, which led to an over-complicated UI. Since then, UI have been simplified. Building words without scaffolding was removed, as not much need for it was observed. Selection of the words to build was consolidated on one panel, which also included speech recognition. Text recognition, word samples and invented spelling were removed as modes of input.

2. Since the phoneme-based blocks and onomatopoeic mnemonics didn't show a definite advantage (see Sysoev, I., Gray, J. H., Fine, S., & Roy, D. (2021). Designing building blocks for open-ended early literacy software. International Journal of Child-Computer Interaction), they were removed in favor of simplifying the interface. If you are interested in that feature, check the paper for the link to the repo with the animations.

3. An "idea button" was incorporated to help kids co-create with the system.

4. Scaffolding system was modified to provide more adaptive, layered scaffolding, akin to Kegel, C. A., & Bus, A. G. (2012). Online tutoring as a pivotal quality of web-based early literacy programs. Journal of Educational Psychology, 104(1), 182. 

5. Scaffolding was also modified to be more flexible and account for invented spelling. When a misspelling occurs, the system pronounces the misspelled words, encouraging exploration and making mistakes more fun.

